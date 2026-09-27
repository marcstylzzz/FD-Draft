using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using FdDraft.Cad;
using FdDraft.Cad.Editing;
using FdDraft.Core.Geometry;
using Line = ACadSharp.Entities.Line;

namespace FdDraft.App
{
    /// <summary>FD Text Edit, Text, FD Layer / Layer Tools and Dimensioning commands.</summary>
    public sealed partial class MainWindow
    {
        // =============================================================================================
        // Text
        // =============================================================================================

        /// <summary>MSCAD's Leroy sizes (the number is hundredths of an inch; the plan size in mm).</summary>
        private static readonly Dictionary<string, double> LeroyMm = new Dictionary<string, double>
        {
            ["050"] = 1.25, ["060"] = 1.5, ["080"] = 2.0, ["100"] = 2.5, ["120"] = 3.0, ["140"] = 3.5, ["175"] = 4.4, ["200"] = 5.0, ["240"] = 6.0,
        };

        /// <summary>The height for new text when none was typed: a Leroy size if one was chosen, else <paramref name="fallback"/>.</summary>
        private double DefaultTextHeight(double fallback, string typed)
        {
            var first = typed.Split(new[] { ' ' }, 2)[0];
            if (typed.Contains(' ') && TryNumber(first, out _)) return fallback; // "height text" typed
            return _settings.TextMm > 0 && _doc != null ? _settings.TextMm * ModelPerMm() : fallback;
        }

        private string? _textStyleName;

        /// <summary>LEROY nnn: resize the selected text to that Leroy size, or make it the size for new text.</summary>
        private void Leroy(string arg)
        {
            var key = arg.Trim().PadLeft(3, '0');
            if (!LeroyMm.TryGetValue(key, out double mm)) { Log("  LEROY 050, 060, 080, 100, 120, 140, 175, 200 or 240"); return; }
            var texts = SelectedEntities().Where(IsText).ToList();
            if (_doc != null && texts.Count > 0)
            {
                double h = mm * ModelPerMm();
                var cmd = SurveyDrafting.SetHeights(texts, _ => h, "Leroy " + key);
                if (cmd == null) { Log("  already Leroy " + key); return; }
                Commit(cmd, "  " + Plural(texts.Count, "text", "texts") + " set to Leroy " + key + " (" + mm.ToString("0.##", CultureInfo.InvariantCulture) + " mm)  (Ctrl+Z undoes it)");
                return;
            }
            _settings.TextMm = mm; _settings.Save();
            Log("  new text will be Leroy " + key + " (" + mm.ToString("0.##", CultureInfo.InvariantCulture) + " mm on paper) - select text first to resize it instead");
        }

        /// <summary>STYLE [name]: list the text styles; apply one to the selected text, or to new text.</summary>
        private void TextStyleCommand(string arg)
        {
            if (!NeedDrawing()) return;
            var styles = _doc!.TextStyles.Select(t => t.Name).Where(n => n.Length > 0).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            void Apply(string name)
            {
                var style = _doc!.TextStyles.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?? (int.TryParse(name, out int i) && i >= 1 && i <= styles.Count ? _doc.TextStyles[styles[i - 1]] : null);
                if (style == null) { Log("  no text style " + name); return; }
                EndTool();
                var texts = SelectedEntities().Where(IsText).ToList();
                if (texts.Count == 0) { _textStyleName = style.Name; Log("  new text uses style " + style.Name + " (" + style.Filename + ")"); return; }
                var cmds = new List<IEditCommand>();
                foreach (var e in texts)
                {
                    if (e is TextEntity t) cmds.Add(new SetPropertyCommand<TextStyle>(t.Style, style, v => t.Style = v, "Text style"));
                    else if (e is MText m) cmds.Add(new SetPropertyCommand<TextStyle>(m.Style, style, v => m.Style = v, "Text style"));
                }
                Commit(new CompositeCommand(cmds, "Text style"), "  " + Plural(texts.Count, "text", "texts") + " now " + style.Name + "  (Ctrl+Z undoes it)");
            }
            if (arg.Length > 0) { Apply(arg.Trim()); return; }
            Log("  text styles: " + string.Join("  ", styles.Select((n, i) => (i + 1) + " " + n + (n.Equals(_textStyleName, StringComparison.OrdinalIgnoreCase) ? " (current)" : ""))));
            BeginTool("STYLE");
            AskText("Text style - number or name" + (SelectedEntities().Any(IsText) ? " for the selected text" : " for new text") + ":", Apply);
        }

        private void StartArrows()
        {
            if (!NeedDrawing()) return;
            double mpm = ModelPerMm();
            Vec2? a = null;
            PickLoop("ARROWS", "Arrows - first point:", "pick two points - a line with arrowheads at both ends (Esc ends)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                if (a == null) { a = model; _canvas.RubberFrom = p; _prompt.Text = "Arrows - second point:"; return; }
                if (Vec2.Distance(a.Value, model.Value) < 1e-9) return;
                AddEntities(SurveyDrafting.ArrowLine(a.Value, model.Value, _settings.ArrowMm * mpm, true, GetOrCreateLayer(CurrentLayer())), "Arrows",
                    "  arrows " + F(Vec2.Distance(a.Value, model.Value)) + "  (Ctrl+Z undoes it)");
                a = null; _canvas.RubberFrom = null; _prompt.Text = "Arrows - first point:";
            });
        }

        private List<Entity>? SelectedTextsOrSay(string command)
        {
            var texts = SelectedEntities().Where(IsText).ToList();
            if (texts.Count == 0) { Log("  select the text first, then " + command); return null; }
            return texts;
        }

        private void StartScaleOne()
        {
            if (!NeedDrawing()) return;
            var texts = SelectedTextsOrSay("SCALEONE");
            if (texts == null) return;
            PickLoop("SCALEONE", "Scale text - pick the text whose size to match:", Plural(texts.Count, "text", "texts") + " selected - pick a text of the size you want", p =>
            {
                var r = PickEntity(p, IsText);
                if (r == null) { Log("  no text there"); return; }
                double h = SurveyDrafting.HeightOf(r);
                EndTool();
                var cmd = SurveyDrafting.SetHeights(texts, _ => h, "Match text size");
                if (cmd == null) { Log("  already that size"); return; }
                Commit(cmd, "  " + Plural(texts.Count, "text", "texts") + " now " + F(h) + " high  (Ctrl+Z undoes it)");
            });
        }

        private void StartScaleText()
        {
            if (!NeedDrawing()) return;
            var texts = SelectedTextsOrSay("SCALETXT");
            if (texts == null) return;
            BeginTool("SCALETXT");
            Log("SCALETXT  " + Plural(texts.Count, "text", "texts") + " - type the factor (2 doubles, 0.5 halves)");
            AskText("Scale text - factor:", s =>
            {
                if (!TryNumber(s, out double k) || k <= 0) { Log("  type a positive factor"); return; }
                EndTool();
                var cmd = SurveyDrafting.SetHeights(texts, h => h * k, "Scale text");
                if (cmd != null) Commit(cmd, "  " + Plural(texts.Count, "text", "texts") + " scaled by " + s.Trim() + "  (Ctrl+Z undoes it)");
            });
        }

        private void RotateTexts180()
        {
            if (!NeedDrawing()) return;
            var texts = SelectedTextsOrSay("ROTEXT");
            if (texts == null) return;
            var cmds = texts.Select(t => SurveyDrafting.Rotate180(t, "Rotate text 180")).Where(c => c != null).Cast<IEditCommand>().ToList();
            Commit(new CompositeCommand(cmds, "Rotate text 180"), "  " + Plural(cmds.Count, "text", "texts") + " turned end for end  (Ctrl+Z undoes it)");
        }

        private void StartRotateToLine()
        {
            if (!NeedDrawing()) return;
            var texts = SelectedTextsOrSay("ROTOLINE");
            if (texts == null) return;
            PickLoop("ROTOLINE", "Rotate text - pick the line or text to match:", Plural(texts.Count, "text", "texts") + " selected - pick the line (or text) they should read along", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var e = PickEntity(p, x => IsText(x) || x is Line || x is LwPolyline || x is Polyline2D);
                if (e == null) { Log("  no line or text there"); return; }
                double rot;
                if (IsText(e)) rot = SurveyDrafting.RotationOf(e);
                else
                {
                    var span = StraightSpanNear(e, model.Value);
                    if (span == null) { Log("  pick a straight part"); return; }
                    rot = Angles.ReadableRotation(span.Value.A, span.Value.B);
                }
                EndTool();
                var cmds = texts.Select(t => SurveyDrafting.SetRotation(t, rot, "Rotate text")).Where(c => c != null).Cast<IEditCommand>().ToList();
                Commit(new CompositeCommand(cmds, "Rotate text"), "  " + Plural(cmds.Count, "text", "texts") + " turned to " + F(rot * 180 / Math.PI, 2) + "°  (Ctrl+Z undoes it)");
            });
        }

        private void StartSlideText()
        {
            if (!NeedDrawing()) return;
            Entity? text = null; Vec2 from = default;
            PickLoop("SLIDETEXT", "Slide text - pick the text:", "pick a text, then where it goes - it slides along its own baseline (Esc ends)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                if (text == null)
                {
                    text = PickEntity(p, IsText);
                    if (text == null) { Log("  no text there"); return; }
                    from = model.Value; _canvas.RubberFrom = p; _prompt.Text = "Slide text - to:";
                    return;
                }
                double r = SurveyDrafting.RotationOf(text);
                var u = new Vec2(Math.Cos(r), Math.Sin(r));
                double along = Vec2.Dot(model.Value - from, u);
                var d = u * along;
                var t = text;
                text = null; _canvas.RubberFrom = null; _prompt.Text = "Slide text - pick the text:";
                Commit(TransformEntitiesCommand.Move(new[] { t }, d.X, d.Y, "Slide text"), "  slid " + F(along) + "  (Ctrl+Z undoes it)");
            });
        }

        private void StartTextEdit()
        {
            if (!NeedDrawing()) return;
            PickLoop("TEXTEDIT", "Edit text - pick the text:", "pick a text, type its new content (Esc ends)", p =>
            {
                var e = PickEntity(p, x => IsText(x) || x is Dimension);
                if (e == null) { Log("  no text there"); return; }
                if (e is Dimension dim) { EditDimensionText(dim); return; }
                string current = e is TextEntity t ? t.Value : ((MText)e).Value;
                Log("  now: " + current);
                AskText("Edit text - new text (Enter keeps it):", s =>
                {
                    if (s.Length > 0 && s != current)
                        Commit(e is TextEntity te ? new EditTextCommand(te, s, "Edit text") : new EditTextCommand((MText)e, s, "Edit text"), "  text changed  (Ctrl+Z undoes it)");
                    StartTextEdit();
                }, blankOk: true);
            });
        }

        private void StartMText()
        {
            if (!NeedDrawing()) return;
            string layer = CurrentLayer();
            PickLoop("MTEXT", "Multiline text - top-left corner:", "pick the top-left corner, then type the text; | or \\P starts a new line (\"height text\" sets the height)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var at = model.Value;
                AskText("Multiline text - text (| between lines):", s =>
                {
                    ParseHeightAndText(s, out double h, out string content);
                    h = DefaultTextHeight(h, s);
                    var m = new MText
                    {
                        Value = content.Replace("|", "\\P"), InsertPoint = new XYZ(at.X, at.Y, 0), Height = h,
                        AttachmentPoint = AttachmentPointType.TopLeft, Layer = GetOrCreateLayer(layer),
                        AlignmentPoint = new XYZ(Math.Cos(-Angles.ViewTwist), Math.Sin(-Angles.ViewTwist), 0),
                    };
                    if (_textStyleName != null && _doc!.TextStyles.TryGetValue(_textStyleName, out var st)) m.Style = st;
                    EndTool();
                    AddEntities(new List<Entity> { m }, "Multiline text", "  multiline text placed");
                });
            });
        }

        private void TextToMText()
        {
            if (!NeedDrawing()) return;
            var texts = SelectedEntities().OfType<TextEntity>().ToList();
            if (texts.Count < 1) { Log("  select the single-line texts to combine first, then TXT2MTXT"); return; }
            double rot = texts[0].Rotation;
            var up = new Vec2(-Math.Sin(rot), Math.Cos(rot));
            var along = new Vec2(Math.Cos(rot), Math.Sin(rot));
            var ordered = texts.OrderByDescending(t => Vec2.Dot(SurveyDrafting.AnchorOf(t), up)).ThenBy(t => Vec2.Dot(SurveyDrafting.AnchorOf(t), along)).ToList();
            double h = ordered[0].Height;
            var top = SurveyDrafting.TextMiddle(ordered[0]) - along * (ordered[0].Value.Length * h * 0.36) + up * (h * 0.5);
            var m = new MText
            {
                Value = string.Join("\\P", ordered.Select(t => t.Value)), InsertPoint = new XYZ(top.X, top.Y, 0), Height = h,
                AttachmentPoint = AttachmentPointType.TopLeft, AlignmentPoint = new XYZ(along.X, along.Y, 0), Layer = ordered[0].Layer, Style = ordered[0].Style,
            };
            var owner = ordered[0].Owner as BlockRecord ?? CurrentEntityOwner();
            var cmd = new CompositeCommand(new IEditCommand[] { new AddEntitiesCommand(owner, new Entity[] { m }, "Text to multiline"), new RemoveEntitiesCommand(ordered.Cast<Entity>().ToList(), "Text to multiline") }, "Text to multiline");
            _canvas.Selected.Clear();
            Commit(cmd, "  " + texts.Count + " lines combined into one multiline text  (Ctrl+Z undoes it)");
        }

        // =============================================================================================
        // Layers
        // =============================================================================================

        private readonly Stack<(Dictionary<string, (bool On, LayerFlags Flags)> Layers, HashSet<string> Hidden)> _layerHistory =
            new Stack<(Dictionary<string, (bool, LayerFlags)>, HashSet<string>)>();
        private List<string> _isolatedOff = new List<string>();

        private void SnapshotLayers()
        {
            if (_doc == null) return;
            _layerHistory.Push((_doc.Layers.ToDictionary(l => l.Name, l => (l.IsOn, l.Flags), StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(_hidden, StringComparer.OrdinalIgnoreCase)));
        }

        private void AfterLayerChange()
        {
            _dirty = true; UpdateTitle();
            FillLayers();
            var c = _canvas.View.Center; var z = _canvas.View.Zoom;
            Rebuild(fit: false);
            _canvas.ZoomTo(c, z);
        }

        private static void SetLayerFlag(Layer l, LayerFlags flag, bool on) => l.Flags = on ? l.Flags | flag : l.Flags & ~flag;

        /// <summary>True for an entity on a locked layer - it can't be selected.</summary>
        private static bool IsLocked(Entity e) => e.Layer != null && e.Layer.Flags.HasFlag(LayerFlags.Locked);

        private bool IsLockedHandle(ulong h) => _doc?.GetCadObject(h) is Entity e && IsLocked(e);

        private void StartLayerPick(string name, Action<Layer> change, string what)
        {
            if (!NeedDrawing()) return;
            PickLoop(name, "Pick an entity on the layer to make " + what + ":", "pick entities - their layers are " + what + " (Esc ends; LAYERP undoes)", p =>
            {
                var e = PickEntity(p, _ => true);
                if (e?.Layer == null) { Log("  nothing there"); return; }
                var layer = e.Layer;
                if (what == "frozen" || what == "off")
                    if (layer.Name.Equals(CurrentLayer(), StringComparison.OrdinalIgnoreCase)) { Log("  " + layer.Name + " is the current layer - it can't be " + what); return; }
                SnapshotLayers();
                change(layer);
                AfterLayerChange();
                Log("  layer " + layer.Name + " " + what);
            });
        }

        private void AllLayers(Action<Layer> change, string what)
        {
            if (!NeedDrawing()) return;
            SnapshotLayers();
            foreach (var l in _doc!.Layers) change(l);
            AfterLayerChange();
            Log("  every layer " + what + "  (LAYERP undoes)");
        }

        private void LayerIsolate()
        {
            if (!NeedDrawing()) return;
            var keep = new HashSet<string>(SelectedEntities().Select(e => e.Layer?.Name ?? "0"), StringComparer.OrdinalIgnoreCase);
            void Isolate()
            {
                SnapshotLayers();
                _isolatedOff = new List<string>();
                foreach (var l in _doc!.Layers)
                    if (!keep.Contains(l.Name) && l.IsOn) { l.IsOn = false; _isolatedOff.Add(l.Name); }
                if (!keep.Contains(CurrentLayer())) _layerCombo.SelectedItem = keep.First();
                AfterLayerChange();
                Log("  isolated " + string.Join(", ", keep) + " - " + _isolatedOff.Count + " layers off (LAYUNISO brings them back)");
            }
            if (keep.Count > 0) { Isolate(); return; }
            PickLoop("LAYISO", "Isolate - pick an entity on each layer to keep (blank when done):", "pick an entity on each layer to keep, blank to isolate them", p =>
            {
                var e = PickEntity(p, _ => true);
                if (e?.Layer == null) return;
                keep.Add(e.Layer.Name);
                Log("  keeping " + e.Layer.Name);
            }, () => { if (keep.Count > 0) Isolate(); });
        }

        private void LayerUnisolate()
        {
            if (!NeedDrawing()) return;
            if (_isolatedOff.Count == 0) { Log("  nothing is isolated"); return; }
            SnapshotLayers();
            foreach (var n in _isolatedOff) if (_doc!.Layers.TryGetValue(n, out var l)) l.IsOn = true;
            Log("  " + _isolatedOff.Count + " layers back on");
            _isolatedOff.Clear();
            AfterLayerChange();
        }

        private void LayerPrevious()
        {
            if (!NeedDrawing()) return;
            if (_layerHistory.Count == 0) { Log("  no layer change to undo"); return; }
            var (layers, hidden) = _layerHistory.Pop();
            foreach (var l in _doc!.Layers)
                if (layers.TryGetValue(l.Name, out var st)) { l.IsOn = st.On; l.Flags = st.Flags; }
            _hidden.Clear(); foreach (var h in hidden) _hidden.Add(h);
            AfterLayerChange();
            Log("  layers back as they were");
        }

        private void StartLayerMatch()
        {
            if (!NeedDrawing()) return;
            var sel = SelectedEntities();
            if (sel.Count == 0) { Log("  select what to move first, then LAYMCH"); return; }
            PickLoop("LAYMCH", "Layer match - pick an entity on the target layer:", Plural(sel.Count, "entity", "entities") + " selected - pick an entity on the layer they go to", p =>
            {
                var e = PickEntity(p, x => !sel.Contains(x));
                if (e?.Layer == null) { Log("  nothing there"); return; }
                EndTool();
                Commit(new ChangeLayerCommand(sel, e.Layer, "Layer match"), "  " + Plural(sel.Count, "entity", "entities") + " moved to layer " + e.Layer.Name + "  (Ctrl+Z undoes it)");
            });
        }

        private void StartLayerCopy()
        {
            if (!NeedDrawing()) return;
            var sel = SelectedEntities();
            if (sel.Count == 0) { Log("  select what to copy first, then LAYCOPY"); return; }
            PickLoop("LAYCOPY", "Copy to layer - pick an entity on the target layer:", Plural(sel.Count, "entity", "entities") + " selected - pick an entity on the layer the copies go on", p =>
            {
                var e = PickEntity(p, x => !sel.Contains(x));
                if (e?.Layer == null) { Log("  nothing there"); return; }
                EndTool();
                var pairs = EntityOps.Copies(sel, 0, 0);
                foreach (var (_, c) in pairs) c.Layer = e.Layer;
                var cmd = EntityOps.AddBesideSources(pairs, "Copy to layer");
                if (cmd == null) { Log("  nothing copyable"); return; }
                Commit(cmd, "  " + Plural(pairs.Count, "copy", "copies") + " on layer " + e.Layer.Name + "  (Ctrl+Z undoes it)");
            });
        }

        private void StartLayerSetCurrent()
        {
            if (!NeedDrawing()) return;
            PickLoop("LAYMCUR", "Set layer - pick an entity on the layer to make current:", "pick an entity - its layer becomes current", p =>
            {
                var e = PickEntity(p, _ => true);
                if (e?.Layer == null) { Log("  nothing there"); return; }
                EndTool();
                if (!_layerCombo.Items.Contains(e.Layer.Name)) FillLayers();
                _layerCombo.SelectedItem = e.Layer.Name;
                Log("  current layer " + e.Layer.Name);
            });
        }

        private void StartLayerWhat()
        {
            if (!NeedDrawing()) return;
            PickLoop("LAYWHAT", "What layer - pick an entity:", "pick entities to see their layer (Esc ends)", p =>
            {
                var e = PickEntity(p, _ => true);
                if (e?.Layer == null) { Log("  nothing there"); return; }
                var l = e.Layer;
                Log("  " + e.GetType().Name.Replace("Entity", "") + " on layer " + l.Name + (l.IsOn ? "" : " (off)") + (l.Flags.HasFlag(LayerFlags.Frozen) ? " (frozen)" : "") + (l.Flags.HasFlag(LayerFlags.Locked) ? " (locked)" : ""));
            });
        }

        private void StartLayerDelete()
        {
            if (!NeedDrawing()) return;
            PickLoop("LAYDEL", "Layer delete - pick an entity on the layer to delete:", "pick an entity - everything on its layer is erased and the layer removed (asks first)", p =>
            {
                var e = PickEntity(p, _ => true);
                if (e?.Layer == null) { Log("  nothing there"); return; }
                var layer = e.Layer;
                if (layer.Name == "0" || layer.Name.Equals(Layer.DefpointsName, StringComparison.OrdinalIgnoreCase) || layer.Name.Equals(CurrentLayer(), StringComparison.OrdinalIgnoreCase))
                { Log("  layer " + layer.Name + " can't be deleted (0, Defpoints and the current layer stay)"); return; }
                var owners = new List<BlockRecord> { _doc!.ModelSpace };
                owners.AddRange(_doc.Layouts.Where(l => l.IsPaperSpace).Select(l => l.AssociatedBlock));
                var victims = owners.SelectMany(b => b.Entities).Where(x => x.Layer == layer).ToList();
                Log("  layer " + layer.Name + ": " + Plural(victims.Count, "entity", "entities") + " in model and on the sheets");
                AskText("Delete layer " + layer.Name + " and everything on it? [Y/N] <N>:", s =>
                {
                    EndTool();
                    if (!s.Trim().StartsWith("Y", StringComparison.OrdinalIgnoreCase)) { Log("  *cancelled*"); return; }
                    var cmds = new List<IEditCommand>();
                    if (victims.Count > 0) cmds.Add(new RemoveEntitiesCommand(victims, "Delete layer"));
                    // The layer itself goes too, unless a block definition still draws on it.
                    bool inBlocks = _doc!.BlockRecords.Where(b => b.Layout == null).SelectMany(b => b.Entities).Any(x => x.Layer == layer);
                    if (!inBlocks) cmds.Add(new LayerTableCommand(_doc, layer));
                    _canvas.Selected.Clear();
                    Commit(new CompositeCommand(cmds, "Delete layer " + layer.Name), "  layer " + layer.Name + " deleted" + (inBlocks ? " (its entities - a block still uses the layer, so it stays in the list)" : "") + "  (Ctrl+Z undoes it)");
                    FillLayers();
                }, blankOk: true);
            });
        }

        /// <summary>Removes a layer from the drawing's layer table, undoably.</summary>
        private sealed class LayerTableCommand : IEditCommand
        {
            private readonly CadDocument _doc; private readonly Layer _layer;
            public string Description => "Delete layer " + _layer.Name;
            public LayerTableCommand(CadDocument doc, Layer layer) { _doc = doc; _layer = layer; Redo(); }
            public void Redo() => _doc.Layers.Remove(_layer.Name);
            public void Undo() { if (!_doc.Layers.Contains(_layer.Name)) _doc.Layers.Add(_layer); }
        }

        /// <summary>LAYERSTATE [SAVE|RESTORE|DELETE name | LIST]: named sets of which layers are on, frozen and locked.</summary>
        private void LayerStateCommand(string arg)
        {
            if (!NeedDrawing()) return;
            var parts = arg.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string op = parts.Length > 0 ? parts[0].ToUpperInvariant() : "";
            string name = parts.Length > 1 ? parts[1].Trim() : "";
            string Encode() => string.Join("|", _doc!.Layers.Select(l => l.Name + "=" + (l.IsOn ? "o" : "") + (l.Flags.HasFlag(LayerFlags.Frozen) ? "f" : "") + (l.Flags.HasFlag(LayerFlags.Locked) ? "l" : "")));
            void Run(string o, string n)
            {
                EndTool();
                switch (o)
                {
                    case "SAVE": case "S":
                        if (n.Length == 0) { BeginTool("LAYERSTATE"); AskText("Save layer state as:", nm => Run("SAVE", nm.Trim())); return; }
                        _settings.LayerStates[n] = Encode(); _settings.Save();
                        Log("  layer state " + n + " saved (" + _doc!.Layers.Count() + " layers)");
                        return;
                    case "RESTORE": case "R":
                        if (!_settings.LayerStates.TryGetValue(n, out var code)) { Log("  no layer state " + n); return; }
                        SnapshotLayers();
                        int applied = 0;
                        foreach (var item in code.Split('|'))
                        {
                            int eq = item.LastIndexOf('=');
                            if (eq <= 0 || !_doc!.Layers.TryGetValue(item.Substring(0, eq), out var l)) continue;
                            var f = item.Substring(eq + 1);
                            l.IsOn = f.Contains('o'); SetLayerFlag(l, LayerFlags.Frozen, f.Contains('f')); SetLayerFlag(l, LayerFlags.Locked, f.Contains('l'));
                            applied++;
                        }
                        AfterLayerChange();
                        Log("  layer state " + n + " restored (" + applied + " layers; LAYERP undoes)");
                        return;
                    case "DELETE": case "D":
                        Log(_settings.LayerStates.Remove(n) ? "  layer state " + n + " deleted" : "  no layer state " + n);
                        _settings.Save();
                        return;
                    default:
                        Log(_settings.LayerStates.Count == 0 ? "  no layer states saved yet - LAYERSTATE SAVE <name>" : "  layer states: " + string.Join(", ", _settings.LayerStates.Keys));
                        return;
                }
            }
            if (op.Length > 0) { Run(op, name); return; }
            Log("  layer states: " + (_settings.LayerStates.Count == 0 ? "(none yet)" : string.Join(", ", _settings.LayerStates.Keys)));
            BeginTool("LAYERSTATE");
            AskText("Layer state - SAVE name, RESTORE name, DELETE name:", s =>
            {
                var p2 = s.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (p2.Length == 1 && _settings.LayerStates.ContainsKey(p2[0])) Run("RESTORE", p2[0]);
                else Run(p2[0].ToUpperInvariant(), p2.Length > 1 ? p2[1] : "");
            });
        }

        private void SetByLayer()
        {
            if (!NeedDrawing()) return;
            var sel = SelectedEntities();
            if (sel.Count == 0) { Log("  select the entities first, then SETBYLAYER"); return; }
            var byLayerType = _doc!.LineTypes.TryGetValue("ByLayer", out var lt) ? lt : null;
            var cmds = new List<IEditCommand>();
            foreach (var e in sel)
            {
                var ent = e;
                if (!ent.Color.IsByLayer) cmds.Add(new SetPropertyCommand<Color>(ent.Color, Color.ByLayer, v => ent.Color = v, "ByLayer"));
                if (ent.LineWeight != LineWeightType.ByLayer) cmds.Add(new SetPropertyCommand<LineWeightType>(ent.LineWeight, LineWeightType.ByLayer, v => ent.LineWeight = v, "ByLayer"));
                if (byLayerType != null && ent.LineType != byLayerType && !ent.LineType.Name.Equals("ByLayer", StringComparison.OrdinalIgnoreCase))
                    cmds.Add(new SetPropertyCommand<LineType>(ent.LineType, byLayerType, v => ent.LineType = v, "ByLayer"));
            }
            if (cmds.Count == 0) { Log("  already ByLayer"); return; }
            Commit(new CompositeCommand(cmds, "Set to ByLayer"), "  colour, linetype and lineweight now ByLayer  (Ctrl+Z undoes it)");
        }

        // =============================================================================================
        // Dimensioning
        // =============================================================================================

        private Dimension? _lastDim;
        private DimensionStyle? _dimStyle;

        private void StartQuickDim()
        {
            if (!NeedDrawing()) return;
            var sel = SelectedEntities().Where(e => e is Line || e is LwPolyline || e is Polyline2D).ToList();
            if (sel.Count == 0) { Log("  select the lines/polylines to dimension first, then QDIM"); return; }
            string layer = CurrentLayer();
            PickLoop("QDIM", "Quick dimension - pick the side the dimensions go:", Plural(sel.Count, "entity", "entities") + " - pick the side to put the dimensions on", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                EndTool();
                double h = _lastDimHeight, off = h * 4;
                var owner = CurrentEntityOwner();
                var cmds = new List<IEditCommand>();
                foreach (var e in sel)
                    foreach (var s in EntityOps.SpansOf(e))
                    {
                        if (s.IsArc || Vec2.Distance(s.A, s.B) < 1e-9) continue;
                        var n = (s.B - s.A).Normalized().Left();
                        if (Vec2.Dot(model.Value - s.A, n) < 0) n = n * -1;
                        var dim = DimensionBuilder.Aligned(s.A, s.B, (s.A + s.B) * 0.5 + n * off, Decimals());
                        if (_dimStyle != null) dim.Style = _dimStyle;
                        dim.Layer = GetOrCreateLayer(layer);
                        cmds.Add(new AddDimensionCommand(owner, dim, h, "Quick dimension"));
                        _lastDim = dim;
                    }
                if (cmds.Count == 0) { Log("  no straight courses in the selection"); return; }
                Commit(new CompositeCommand(cmds, "Quick dimension"), "  " + Plural(cmds.Count, "dimension", "dimensions") + " placed  (Ctrl+Z undoes them)");
            });
        }

        /// <summary>DIMBASELINE / DIMCONTINUE from the last linear or aligned dimension.</summary>
        private void StartDimChain(bool baseline)
        {
            if (!NeedDrawing()) return;
            if (!(_lastDim is DimensionAligned || _lastDim is DimensionLinear) || _lastDim.Owner == null || _lastDim.Document != _doc)
            {
                Log("  draw a linear or aligned dimension first (DIMLIN / DIM) - " + (baseline ? "baseline" : "continue") + " carries on from it");
                return;
            }
            string layer = CurrentLayer();
            string name = baseline ? "DIMBASELINE" : "DIMCONTINUE";
            PickLoop(name, "Dimension " + (baseline ? "baseline" : "continue") + " - next point:", "pick the next points to dimension to (Esc ends)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var last = _lastDim!;
                Vec2 V(XYZ q) => new Vec2(q.X, q.Y);
                Vec2 p1 = V(last is DimensionLinear l1 ? l1.FirstPoint : ((DimensionAligned)last).FirstPoint);
                Vec2 p2 = V(last is DimensionLinear l2 ? l2.SecondPoint : ((DimensionAligned)last).SecondPoint);
                Vec2 def = V(last.DefinitionPoint);
                // Which way is "out": from the measured points toward the dimension line.
                var outward = (def - p2).Length > 1e-9 ? (def - p2).Normalized() : (p2 - p1).Normalized().Left();
                double spacing = _lastDimHeight * 3;
                Vec2 start = baseline ? p1 : p2;
                Vec2 linePoint = baseline ? def + outward * spacing : def;
                Dimension dim = last is DimensionLinear lin
                    ? DimensionBuilder.Linear(start, model.Value, linePoint, lin.Rotation, Decimals())
                    : DimensionBuilder.Aligned(start, model.Value, linePoint, Decimals());
                if (_dimStyle != null) dim.Style = _dimStyle;
                dim.Layer = GetOrCreateLayer(layer);
                Commit(new AddDimensionCommand(CurrentEntityOwner(), dim, _lastDimHeight, "Dimension"), "  dimension " + dim.Text + " placed");
                _lastDim = dim;
            });
        }

        private void StartCenterMark()
        {
            if (!NeedDrawing()) return;
            double mpm = ModelPerMm();
            PickLoop("CENTERMARK", "Center mark - pick an arc or circle:", "pick arcs/circles to mark their centres (Esc ends)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                if (!(PickEntity(p, x => x is Circle) is Circle c)) { Log("  no arc or circle there"); return; }
                double s = Math.Min(c.Radius * 0.25, 2.5 * mpm);
                var ctr = new Vec2(c.Center.X, c.Center.Y);
                var layer = GetOrCreateLayer(CurrentLayer());
                var list = new List<Entity>
                {
                    new Line(new XYZ(ctr.X - s, ctr.Y, 0), new XYZ(ctr.X + s, ctr.Y, 0)) { Layer = layer },
                    new Line(new XYZ(ctr.X, ctr.Y - s, 0), new XYZ(ctr.X, ctr.Y + s, 0)) { Layer = layer },
                };
                AddEntities(list, "Center mark", "  centre " + NE(ctr) + "  R " + F(c.Radius));
            });
        }

        private void StartCenterLine()
        {
            if (!NeedDrawing()) return;
            (Vec2 A, Vec2 B)? first = null;
            PickLoop("CENTERLINE", "Center line - pick the first line:", "pick two lines - a centre line midway between them (Esc ends)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                var e = PickEntity(p, x => x is Line || x is LwPolyline || x is Polyline2D);
                var span = e == null ? null : StraightSpanNear(e, model.Value);
                if (span == null) { Log("  no line there"); return; }
                if (first == null) { first = span; _prompt.Text = "Center line - pick the second line:"; return; }
                var (a1, b1) = first.Value; var (a2, b2) = span.Value;
                // Pair each end with the nearer end of the other line.
                if (Vec2.Distance(a1, a2) + Vec2.Distance(b1, b2) > Vec2.Distance(a1, b2) + Vec2.Distance(b1, a2)) (a2, b2) = (b2, a2);
                var s = (a1 + a2) * 0.5; var t = (b1 + b2) * 0.5;
                first = null; _prompt.Text = "Center line - pick the first line:";
                AddEntities(new List<Entity> { new Line(new XYZ(s.X, s.Y, 0), new XYZ(t.X, t.Y, 0)) { Layer = GetOrCreateLayer(CurrentLayer()) } }, "Center line", "  centre line " + F(Vec2.Distance(s, t)));
            });
        }

        private Dimension? PickOurDimension(Vec2 p)
        {
            var d = PickEntity(p, x => x is Dimension) as Dimension;
            if (d == null) { Log("  no dimension there"); return null; }
            if (!DimensionBuilder.IsOurs(d)) { Log("  that dimension came with the drawing in a kind FD-Draft doesn't redraw - edit it in its own program"); return null; }
            return d;
        }

        private void EditDimensionText(Dimension dim)
        {
            Log("  now: " + (dim.Text ?? "") + "   (type new text; <> is the measurement; blank puts the measurement back)");
            AskText("Dimension text:", s =>
            {
                var cmd = DimensionBuilder.SetText(dim, s, Decimals(), "Dimension text");
                if (cmd != null) Commit(cmd, "  dimension text now " + dim.Text + "  (Ctrl+Z undoes it)");
                EndTool();
            }, blankOk: true);
        }

        private void StartDimText()
        {
            if (!NeedDrawing()) return;
            PickLoop("DIMTEXT", "Dimension text - pick the dimension:", "pick a dimension, then type its new text", p =>
            {
                var d = PickOurDimension(p);
                if (d != null) EditDimensionText(d);
            });
        }

        private void StartDimRotate()
        {
            if (!NeedDrawing()) return;
            PickLoop("DIMROTATE", "Rotate dimension text - pick the dimension:", "pick a dimension, then type the text angle in degrees (0 = reads east)", p =>
            {
                var d = PickOurDimension(p);
                if (d == null) return;
                AskText("Dimension text angle, degrees:", s =>
                {
                    if (!TryNumber(s, out double deg)) { Log("  type an angle in degrees"); return; }
                    var cmd = DimensionBuilder.PlaceText(d, null, deg * Math.PI / 180, false, "Rotate dimension text");
                    EndTool();
                    if (cmd != null) Commit(cmd, "  dimension text at " + s.Trim() + "°  (Ctrl+Z undoes it)");
                });
            });
        }

        private void StartDimTextMove()
        {
            if (!NeedDrawing()) return;
            Dimension? dim = null;
            PickLoop("DIMTEDIT", "Reposition dimension text - pick the dimension:", "pick a dimension, then where its text goes (Esc ends)", p =>
            {
                var model = ModelOf(p);
                if (model == null) return;
                if (dim == null) { dim = PickOurDimension(p); if (dim != null) { _prompt.Text = "Reposition dimension text - new text position:"; _canvas.RubberFrom = p; } return; }
                var cmd = DimensionBuilder.PlaceText(dim, model.Value, null, false, "Move dimension text");
                dim = null; _canvas.RubberFrom = null; _prompt.Text = "Reposition dimension text - pick the dimension:";
                if (cmd != null) Commit(cmd, "  dimension text moved  (DIMHOME puts it back; Ctrl+Z undoes it)");
            });
        }

        private void StartDimHome()
        {
            if (!NeedDrawing()) return;
            PickLoop("DIMHOME", "Restore dimension text - pick a dimension:", "pick dimensions to put their text back in place (Esc ends)", p =>
            {
                var d = PickOurDimension(p);
                if (d == null) return;
                var cmd = DimensionBuilder.PlaceText(d, null, null, true, "Restore dimension text");
                if (cmd != null) Commit(cmd, "  dimension text restored");
            });
        }

        private void DimStyleCommand(string arg)
        {
            if (!NeedDrawing()) return;
            var styles = _doc!.DimensionStyles.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            void Apply(string n)
            {
                EndTool();
                var st = styles.FirstOrDefault(s => s.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
                    ?? (int.TryParse(n, out int i) && i >= 1 && i <= styles.Count ? styles[i - 1] : null);
                if (st == null) { Log("  no dimension style " + n); return; }
                _dimStyle = st;
                Log("  new dimensions use style " + st.Name + " (DIMUPDATE applies it to selected ones)");
            }
            if (arg.Length > 0) { Apply(arg.Trim()); return; }
            Log("  dimension styles: " + string.Join("  ", styles.Select((s, i) => (i + 1) + " " + s.Name + (s == _dimStyle ? " (current)" : ""))));
            BeginTool("DIMSTYLE");
            AskText("Dimension style - number or name:", Apply);
        }

        private void DimStatus()
        {
            if (!NeedDrawing()) return;
            var st = _dimStyle ?? _doc!.DimensionStyles.FirstOrDefault(s => s.Name.Equals("Standard", StringComparison.OrdinalIgnoreCase)) ?? _doc!.DimensionStyles.FirstOrDefault();
            if (st == null) { Log("  this drawing has no dimension styles"); return; }
            Log("  dimension style " + st.Name + (st == _dimStyle ? " (current)" : ""));
            Log("    text height " + F(st.TextHeight) + "   arrow size " + F(st.ArrowSize) + "   overall scale " + F(st.ScaleFactor) + "   linear scale " + F(st.LinearScaleFactor));
            Log("    extension beyond line " + F(st.ExtensionLineExtension) + "   extension offset " + F(st.ExtensionLineOffset) + "   text gap " + F(st.DimensionLineGap) + "   decimals " + st.DecimalPlaces);
            Log("    text style " + (st.Style?.Name ?? "-") + "   FD-Draft draws its own dimensions at " + F(_lastDimHeight) + " text height (asked when placing one)");
        }

        private void DimUpdate()
        {
            if (!NeedDrawing()) return;
            var dims = SelectedEntities().OfType<Dimension>().ToList();
            if (dims.Count == 0) { Log("  select the dimensions first, then DIMUPDATE"); return; }
            if (_dimStyle == null) { Log("  choose a style first with DIMSTYLE"); return; }
            var st = _dimStyle;
            var cmds = dims.Select(d => (IEditCommand)new SetPropertyCommand<DimensionStyle>(d.Style, st, v => d.Style = v, "Dimension style")).ToList();
            Commit(new CompositeCommand(cmds, "Update dimensions"), "  " + Plural(dims.Count, "dimension", "dimensions") + " now style " + st.Name + "  (Ctrl+Z undoes it)");
        }
    }
}
