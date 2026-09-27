using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Cad;
using FdDraft.Cad.Editing;
using FdDraft.Core.Geometry;

namespace FdDraft.App
{
    /// <summary>
    /// The right-click menu's MSCAD extras: double-click to edit a text whole, Draw Order,
    /// Select Similar, and the Clipboard (cut / copy / copy with base point / paste / paste as
    /// block / paste to original coordinates).
    /// </summary>
    public sealed partial class MainWindow
    {
        // ---- double-click ------------------------------------------------------------------------

        private void OnCanvasDoubleClick(ulong handle)
        {
            if (_doc?.GetCadObject(handle) is not Entity e) return;
            _canvas.Selected.Clear(); _canvas.Selected.Add(handle);
            UpdateProperties(); _canvas.InvalidateVisual();
            switch (e)
            {
                case TextEntity t when !(t is AttributeEntity) && !(t is AttributeDefinition):
                    EditTextWhole(t.Value, false, v => Commit(new EditTextCommand(t, v, "Edit text"), "  text changed  (Ctrl+Z undoes it)"));
                    break;
                case MText m:
                    EditTextWhole(m.Value, true, v => Commit(new EditTextCommand(m, v, "Edit text"), "  text changed  (Ctrl+Z undoes it)"));
                    break;
                case Dimension d:
                    EditDimensionText(d);
                    break;
                default:
                    if (_propertiesTab != null) _leftTabs.SelectedItem = _propertiesTab;
                    break;
            }
        }

        /// <summary>The whole text (every line of an MTEXT) in an editor; OK applies it.</summary>
        private void EditTextWhole(string value, bool multiline, Action<string> apply)
        {
            string shown = multiline ? value.Replace("\\P", Environment.NewLine) : value;
            var box = new TextBox
            {
                Text = shown, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinWidth = 460, MinHeight = multiline ? 160 : 0, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 0, 0, 8),
            };
            var w = new Window
            {
                Title = multiline ? "Edit multiline text" : "Edit text", Owner = this, SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResizeWithGrip, ShowInTaskbar = false,
            };
            var ok = new Button { Content = "OK", IsDefault = !multiline, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
            ok.Click += (s, e) => { w.DialogResult = true; };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            var root = new StackPanel { Margin = new Thickness(12) };
            if (multiline) root.Children.Add(new TextBlock { Text = "Enter starts a new line. Formatting codes ({\\H...;} etc.) are kept as typed.", Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 0, 0, 6) });
            root.Children.Add(box); root.Children.Add(buttons);
            w.Content = root;
            w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            if (w.ShowDialog() != true) return;
            string result = multiline ? box.Text.Replace("\r\n", "\\P").Replace("\n", "\\P") : box.Text;
            if (result != value) apply(result);
        }

        // ---- draw order ---------------------------------------------------------------------------

        private void DrawOrder(DrawOrderCommand.Place place)
        {
            var sel = SelectedEntities();
            if (sel.Count == 0) { Log("  select what to reorder first"); return; }
            var groups = sel.Where(e => e.Owner is BlockRecord).GroupBy(e => (BlockRecord)e.Owner!).ToList();
            if (place == DrawOrderCommand.Place.Front || place == DrawOrderCommand.Place.Back)
            {
                var cmds = groups.Select(g => (IEditCommand)new DrawOrderCommand(g.Key, g.ToList(), place, null, "Draw order")).ToList();
                Commit(cmds.Count == 1 ? cmds[0] : new CompositeCommand(cmds, "Draw order"), "  " + Plural(sel.Count, "entity", "entities") + " brought to the " + (place == DrawOrderCommand.Place.Front ? "front" : "back") + "  (Ctrl+Z undoes it)");
                return;
            }
            string what = place == DrawOrderCommand.Place.Above ? "above" : "under";
            PickLoop("DRAWORDER", "Draw order - pick the object to go " + what + ":", "pick the reference object (Esc cancels)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var reference = PickEntity(p, e => !sel.Contains(e));
                if (reference == null) { Log("  nothing there - pick the object to go " + what); return; }
                EndTool();
                var owner = reference.Owner as BlockRecord;
                var same = sel.Where(e => e.Owner == owner).ToList();
                if (owner == null || same.Count == 0) { Log("  that object is in another space from the selection"); return; }
                Commit(new DrawOrderCommand(owner, same, place, reference, "Draw order"), "  " + Plural(same.Count, "entity", "entities") + " moved " + what + " it  (Ctrl+Z undoes it)");
            });
        }

        // ---- select similar -----------------------------------------------------------------------

        /// <summary>Everything in the same space like the selection: same kind of object on the same
        /// layer (and the same block, or the same text style).</summary>
        private void SelectSimilar()
        {
            var sel = SelectedEntities();
            if (sel.Count == 0) { Log("  select an example first"); return; }
            string Key(Entity e) => e.GetType().Name + "|" + (e.Layer?.Name ?? "0") + "|" + e switch
            {
                Insert i => i.Block?.Name ?? "",
                TextEntity t => t.Style?.Name ?? "",
                MText m => m.Style?.Name ?? "",
                _ => "",
            };
            var keys = new HashSet<string>(sel.Select(Key));
            var owners = sel.Select(e => e.Owner).OfType<BlockRecord>().Distinct().ToList();
            int n = 0;
            foreach (var o in owners)
                foreach (var e in o.Entities)
                    if (keys.Contains(Key(e)) && _canvas.Selected.Add(e.Handle)) n++;
            UpdateProperties(); _canvas.InvalidateVisual();
            Log("  " + _canvas.Selected.Count + " selected (" + n + " more like it)");
        }

        // ---- clipboard ----------------------------------------------------------------------------

        /// <summary>What was cut or copied: copies in a drawing of their own (so they paste into
        /// another open drawing too), and the base point they paste by.</summary>
        private static CadDocument? _clipDoc;
        private static Vec2 _clipBase;
        private static bool _clipFromPaper;

        private void ClipCopy(bool askBase, bool cut = false)
        {
            var sel = SelectedEntities();
            if (sel.Count == 0) { Log("  select what to " + (cut ? "cut" : "copy") + " first"); return; }
            void Store(Vec2 basePoint)
            {
                var doc = new CadDocument();
                foreach (var e in sel)
                    if (TitleBlocks.Copy(e, doc) is Entity c) doc.ModelSpace.Entities.Add(c);
                _clipDoc = doc; _clipBase = basePoint;
                _clipFromPaper = sel[0].Owner is BlockRecord b && b != _doc!.ModelSpace;
                if (cut)
                {
                    _canvas.Selected.Clear();
                    Commit(new RemoveEntitiesCommand(sel, "Cut"), "  " + Plural(sel.Count, "entity", "entities") + " cut to the clipboard  (Ctrl+V pastes; Ctrl+Z undoes)");
                }
                else Log("  " + Plural(sel.Count, "entity", "entities") + " copied to the clipboard  (Ctrl+V pastes)");
            }
            if (!askBase)
            {
                // As AutoCAD: the base point is the lower-left of what was copied.
                double x = double.MaxValue, y = double.MaxValue;
                foreach (var e in sel)
                {
                    try { var bb = e.GetBoundingBox(); x = Math.Min(x, bb.Min.X); y = Math.Min(y, bb.Min.Y); } catch (Exception) { }
                }
                Store(new Vec2(x == double.MaxValue ? 0 : x, y == double.MaxValue ? 0 : y));
                return;
            }
            PickLoop("COPYBASE", "Copy with base point - pick the base point:", "pick the base point (Esc cancels)", p =>
            {
                var pt = ClipPoint(p);
                if (pt == null) return;
                EndTool();
                Store(pt.Value);
            });
        }

        /// <summary>Model coordinates for a pick where the entities go in model space, sheet
        /// coordinates where they go on the sheet itself.</summary>
        private Vec2? ClipPoint(Vec2 scenePoint) => CurrentEntityOwner() == _doc!.ModelSpace ? ModelOf(scenePoint) : scenePoint;

        private enum PasteMode { AtPoint, AsBlock, Original }

        private void ClipPaste(PasteMode mode)
        {
            if (!NeedDrawing()) return;
            if (_clipDoc == null || _clipDoc.ModelSpace.Entities.Count == 0) { Log("  the clipboard is empty - cut or copy something first"); return; }
            var owner = CurrentEntityOwner();
            var clip = _clipDoc;
            void Place(Vec2 at)
            {
                var d = at - _clipBase;
                var added = new List<Entity>();
                if (mode == PasteMode.AsBlock)
                {
                    int k = 1;
                    while (_doc!.BlockRecords.Contains("FD-PASTE-" + k)) k++;
                    var blk = new BlockRecord("FD-PASTE-" + k);
                    _doc.BlockRecords.Add(blk);
                    var toBase = Transform.CreateTranslation(new XYZ(-_clipBase.X, -_clipBase.Y, 0));
                    foreach (var e in clip.ModelSpace.Entities)
                        if (TitleBlocks.Copy(e, _doc) is Entity c) { EntityTransform.Apply(c, toBase); blk.Entities.Add(c); }
                    added.Add(new Insert(blk) { InsertPoint = new XYZ(at.X, at.Y, 0), Layer = GetOrCreateLayer(CurrentLayer()) });
                }
                else
                {
                    var move = Transform.CreateTranslation(new XYZ(d.X, d.Y, 0));
                    foreach (var e in clip.ModelSpace.Entities)
                        if (TitleBlocks.Copy(e, _doc!) is Entity c) { if (mode == PasteMode.AtPoint) EntityTransform.Apply(c, move); added.Add(c); }
                }
                Commit(new AddEntitiesCommand(owner, added, "Paste"), "  pasted " + Plural(added.Count, "entity", "entities") + (mode == PasteMode.AsBlock ? " as a block" : "") + "  (Ctrl+Z undoes it)");
                _canvas.Selected.Clear();
                foreach (var e in added) _canvas.Selected.Add(e.Handle);
                _canvas.InvalidateVisual();
            }
            if (mode == PasteMode.Original) { Place(_clipBase); return; }
            PickLoop("PASTE", mode == PasteMode.AsBlock ? "Paste as block - insertion point:" : "Paste - insertion point:", "pick where the base point goes (Esc cancels)", p =>
            {
                var pt = ClipPoint(p);
                if (pt == null) return;
                EndTool();
                Place(pt.Value);
            });
        }

        /// <summary>The Draw Order and Clipboard submenus for the right-click menu.</summary>
        private void AddContextExtras(ContextMenu menu, bool any)
        {
            MenuItem Item(string header, string gesture, Action a, bool enabled = true)
            {
                var m = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
                m.Click += (s, e) => a();
                return m;
            }
            var clip = new MenuItem { Header = "Clipboard" };
            bool full = _clipDoc != null && _clipDoc.ModelSpace.Entities.Count > 0;
            clip.Items.Add(Item("Cut", "Ctrl+X", () => ClipCopy(false, cut: true), any));
            clip.Items.Add(Item("Copy", "Ctrl+C", () => ClipCopy(false), any));
            clip.Items.Add(Item("Copy with Base Point", "Ctrl+Shift+C", () => ClipCopy(true), any));
            clip.Items.Add(Item("Paste", "Ctrl+V", () => ClipPaste(PasteMode.AtPoint), full));
            clip.Items.Add(Item("Paste as Block", "Ctrl+Shift+V", () => ClipPaste(PasteMode.AsBlock), full));
            clip.Items.Add(Item("Paste to Original Coordinates", "", () => ClipPaste(PasteMode.Original), full));
            menu.Items.Add(clip);
            if (!any) return;
            var order = new MenuItem { Header = "Draw Order" };
            order.Items.Add(Item("Front", "", () => DrawOrder(DrawOrderCommand.Place.Front)));
            order.Items.Add(Item("Back", "", () => DrawOrder(DrawOrderCommand.Place.Back)));
            order.Items.Add(Item("Above", "pick object", () => DrawOrder(DrawOrderCommand.Place.Above)));
            order.Items.Add(Item("Under", "pick object", () => DrawOrder(DrawOrderCommand.Place.Under)));
            menu.Items.Add(order);
            menu.Items.Add(Item("Select Similar", "SELECTSIMILAR", SelectSimilar));
        }
    }
}
