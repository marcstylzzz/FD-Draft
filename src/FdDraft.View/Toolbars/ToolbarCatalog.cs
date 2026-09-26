using System.Collections.Generic;
using System.Linq;

namespace FdDraft.View.Toolbars
{
    public enum ToolButtonKind
    {
        /// <summary>Runs <see cref="ToolButton.Command"/> as if typed.</summary>
        Command,
        /// <summary>An on/off button the app keeps in step (object snap modes, PAN).</summary>
        Toggle,
        /// <summary>A control the app builds itself (the current-layer list).</summary>
        Custom,
        Separator,
    }

    public sealed class ToolButton
    {
        public ToolButtonKind Kind = ToolButtonKind.Command;
        /// <summary>Icon key (<see cref="ToolIcons"/>).</summary>
        public string Icon = "";
        public string Name = "";
        /// <summary>What it does in FD-Draft (the tooltip).</summary>
        public string Help = "";
        /// <summary>The FD-Draft command it runs, typed-command style ("LAYISO", "LEROY 080"). Null
        /// when FD-Draft has no equivalent yet - the button is still shown, dimmed, so the bar keeps
        /// MSCAD's layout.</summary>
        public string? Command;
        /// <summary>The MSCAD / IntelliCAD command behind the original button (from icad.cui).</summary>
        public string? Mscad;
        /// <summary>For an unavailable button: why.</summary>
        public string Note = "";

        public bool Available => Kind != ToolButtonKind.Command || Command != null;
    }

    public sealed class ToolbarDef
    {
        public string Key = "";
        public string Name = "";
        /// <summary>Default row in the toolbar tray (0 = top).</summary>
        public int Band;
        /// <summary>True for the survey bars laid out after MSCAD's (icad.cui) - FD Labels, FD Ties...</summary>
        public bool FromMscad;
        public List<ToolButton> Buttons = new List<ToolButton>();
    }

    /// <summary>
    /// Every toolbar and button in FD-Draft, in order. The MSCAD bars follow MicroSurvey CAD's
    /// icad.cui button for button (Marc's MSCAD install), mapped to FD-Draft commands; buttons
    /// with no FD-Draft equivalent keep their place, dimmed, with the reason in the tooltip.
    /// </summary>
    public static class ToolbarCatalog
    {
        private static ToolButton B(string icon, string name, string? command, string help, string? mscad = null) =>
            new ToolButton { Icon = icon, Name = name, Command = command, Help = help, Mscad = mscad };

        private static ToolButton NA(string icon, string name, string mscad, string note) =>
            new ToolButton { Icon = icon, Name = name, Command = null, Mscad = mscad, Note = note };

        private static ToolButton T(string icon, string name, string command, string help) =>
            new ToolButton { Kind = ToolButtonKind.Toggle, Icon = icon, Name = name, Command = command, Help = help };

        private static readonly ToolButton Sep = new ToolButton { Kind = ToolButtonKind.Separator };

        private static ToolbarDef Bar(string key, string name, int band, bool mscad, params ToolButton[] buttons) =>
            new ToolbarDef { Key = key, Name = name, Band = band, FromMscad = mscad, Buttons = buttons.ToList() };

        private static List<ToolbarDef>? _all;

        public static IReadOnlyList<ToolbarDef> All => _all ??= Build();

        public static ToolbarDef? Find(string key) => All.FirstOrDefault(b => b.Key == key);

        private const string NoTraverse = "FD-Draft drafts from FD-Pro, which owns the raw data and traverse adjustment";
        private const string NoPointDb = "FD-Draft reads points from the FD-Pro job; edit them in FD-Pro";

        private static List<ToolbarDef> Build() => new List<ToolbarDef>
        {
            // ---------------------------------------------------------------- band 0: FD-Draft's own
            Bar("standard", "Standard", 0, false,
                B("draft", "Draft FD-Pro Job", "DRAFT", "Draft an FD-Pro job onto the firm template (Ctrl+D)"),
                Sep,
                B("new", "New from Template", "NEW", "Start a new drawing from a .dwt template (Ctrl+N)"),
                B("open", "Open", "OPEN", "Open a DWG or DWT (Ctrl+O)"),
                B("save", "Save", "SAVE", "Save the DWG (Ctrl+S)"),
                B("print", "Print / Plot", "PRINT", "Print or plot: printer or PDF, paper, .ctb, area, scale (Ctrl+P)"),
                Sep,
                B("undo", "Undo", "UNDO", "Undo the last change (Ctrl+Z)"),
                B("redo", "Redo", "REDO", "Redo (Ctrl+Y)")),
            Bar("draw", "Draw", 0, false,
                B("line", "Line", "LINE", "Lines by bearing and distance, or picked points (LINE)"),
                B("arc", "Arc", "ARC", "Arc through three points (ARC)"),
                B("text", "Text", "TEXT", "Place text (TEXT)"),
                B("leader", "Leader", "LEADER", "Leader with text (LEADER)"),
                B("dim_aligned", "Dimension", "DIM", "Aligned dimension (DIM; DIMLIN linear, DIMRAD radius)")),
            Bar("modify", "Modify", 0, false,
                B("erase", "Erase", "ERASE", "Erase the selected entities (Del)"),
                B("move", "Move", "MOVE", "Move the selection (MOVE)"),
                B("copy", "Copy", "COPY", "Copy the selection, to as many places as you pick (COPY)"),
                B("rotate", "Rotate", "ROTATE", "Rotate the selection about a point (ROTATE)"),
                B("mirror", "Mirror", "MIRROR", "Mirror the selection across a line (MIRROR)"),
                B("stretch", "Stretch", "STRETCH", "Move one shared vertex, keeping lines joined (STRETCH)"),
                B("offset", "Offset", "OFFSET", "Parallel copy at a distance (OFFSET)"),
                B("trim", "Trim", "TRIM", "Cut lines back at edges (TRIM)"),
                B("extend", "Extend", "EXTEND", "Run lines out to a boundary (EXTEND)"),
                B("fillet", "Fillet", "FILLET", "Round or close the corner between two lines (FILLET)"),
                B("join", "Join", "JOIN", "Join touching lines/arcs into a polyline (JOIN)")),
            Bar("layer", "Layer", 0, false,
                new ToolButton { Kind = ToolButtonKind.Custom, Command = "LAYERCOMBO", Name = "Current layer", Help = "The layer new work goes on" },
                B("lay_cur", "Set Layer", "LAYCUR", "Put the selection on the current layer (LAYCUR)")),
            Bar("view", "View", 0, false,
                B("regen", "Regen", "REGEN", "Redraw from the drawing (REGEN)"),
                T("pan", "Pan", "PAN", "Pan mode: drag to pan (PAN; Esc or click again to stop). Middle-drag always pans."),
                B("zoomwin", "Zoom Window", "ZW", "Zoom to a box (ZW)"),
                B("zoomprev", "Zoom Previous", "ZP", "Back to the previous view (ZP)"),
                B("zoomin", "Zoom In", "ZI", "Zoom in (ZI) - the wheel zooms at the cursor"),
                B("zoomout", "Zoom Out", "ZO", "Zoom out (ZO)"),
                B("zoomext", "Zoom Extents", "ZE", "Zoom to everything (ZE, or double-click the wheel)")),
            Bar("snaps", "Object Snap", 1, false,
                T("snap_end", "Endpoint", "SNAPMODE END", "Endpoint: line and polyline ends, arc ends"),
                T("snap_mid", "Midpoint", "SNAPMODE MID", "Midpoint of a line or polyline span"),
                T("snap_int", "Intersection", "SNAPMODE INT", "Where two lines (or a line and a circle) cross"),
                T("snap_cen", "Center", "SNAPMODE CEN", "Centre of a circle or arc"),
                T("snap_quad", "Quadrant", "SNAPMODE QUA", "A circle's north, south, east or west point"),
                T("snap_perp", "Perpendicular", "SNAPMODE PER", "Foot of the perpendicular from the last point"),
                T("snap_near", "Nearest", "SNAPMODE NEA", "Nearest point on a line or circle"),
                T("snap_node", "Node", "SNAPMODE NOD", "Survey points and point objects"),
                B("snap_none", "Snaps Off", "SNAPMODE NONE", "Turn every object snap off (F3 still switches snapping on/off)")),

            // ---------------------------------------------------------------- band 1
            Bar("survey", "Survey", 1, false,
                B("inverse", "Inverse", "INV", "Bearing and distance between picked points (INV)"),
                B("area", "Area", "AREA", "Area and perimeter of a closed figure or picked corners (AREA)"),
                B("id", "ID Point", "ID", "Coordinates of a picked point (ID)"),
                B("label", "Label Selection", "LABEL", "Bearing/distance or curve labels for the selection, by the firm's rules (LABEL)"),
                B("flip", "Flip Label", "FLIP", "Move the selected labels to the other side of their course (FLIP)")),
            Bar("labels", "FD Labels", 1, true,
                B("split_brg", "Auto Split Bearing", "SPLITBRG", "Bearing split across the line: direction and degrees on one side, minutes and seconds on the other (pick lines)", "_split_bearings"),
                B("brg_on", "Bearing on Centre of Line", "BRGON", "Bearing centred on the line, the line broken around it (pick lines)", "(bear_dist_label 5)"),
                B("brg_off", "Auto Bearing off Line", "BRGOFF", "Bearing beside the line, on the side you pick", "_ms_bear_off_line"),
                B("dist_on", "Auto Distance", "DISTON", "Distance centred on the line, the line broken around it", "(bear_dist_label 6)"),
                B("dist_off", "Auto Distance off Line", "DISTOFF", "Distance beside the line, on the side you pick", "_ms_dist_off_line"),
                B("brg_dist", "Auto Bearing/Distance", "BRGDIST", "Bearing on one side of the line, distance on the other", "(bear_dist_label 4)"),
                B("brg_dash_dist", "Auto Bearing - Distance", "BRGDASH", "Bearing then distance, one line of text on the picked side", "(bear_dist_label 2)"),
                B("dist_dash_brg", "Auto Distance - Bearing", "DISTDASH", "Distance then bearing, one line of text on the picked side", "(bear_dist_label 1)"),
                B("brg_over_dist", "Auto Bearing/Distance // Line", "BRGDISTL", "Bearing over distance, both on the picked side", "(bear_dist_label 7)"),
                B("dist_over_brg", "Auto Distance/Bearing // Line", "DISTBRGL", "Distance over bearing, both on the picked side", "(bear_dist_label 3)"),
                B("add_angle", "Auto Add Angle", "ADDANGLE", "Angle between two lines: pick each line, then where the label goes", "_ms_auto_angle"),
                B("arrows_line", "Auto Arrows on Line", "ARROWLINE", "Arrow alongside the line, clear of the labels, on the picked side", "(bear_dist_label 8)"),
                B("curve_on", "Label on Curve", "CURVEON", "Curve data following the arc (pick arcs)", "_ms_man_curve_label_on_arc"),
                B("curve_off", "Label off Curve", "CURVEOFF", "Curve data as a block of text placed anywhere: pick the arc, then the spot", "_ms_curve_label_off_arc"),
                B("text_arc", "Text on Arc", "TEXTARC", "Text that follows an arc: pick the arc, type the text", "_ms_text_on_arc")),
            Bar("ties", "FD Ties", 1, true,
                B("house_tie", "Auto House Tie with Arrows", "HOUSETIEA", "Ties from a building to the lot lines, with arrows: pick the building, then each lot line", "_ms_house_tie_arrows"),
                B("house_tie_plain", "Auto House Tie", "HOUSETIE", "Ties from a building to the lot lines, no arrows", "_ms_house_tie"),
                B("mhouse_tie_a", "Manual House Tie with Arrows", "MHOUSETIEA", "Pick a building corner, then the lot line: a square tie with arrows", "(ms_man_house_tie_arrows 2)"),
                B("mhouse_tie", "Manual House Tie", "MHOUSETIE", "Pick a building corner, then the lot line: a square tie, no arrows", "(ms_man_house_tie_arrows 1)"),
                B("leader_scale", "Leader Scale", "LEADERSCALE", "Arrow size (paper mm) for leaders, ties and arrows", "_set_leader_scale"),
                B("curvy_leader", "Curvy Leader", "CURVYLEADER", "A curved leader with an arrowhead, then text", "_ms_curvey_leader"),
                B("straight_leader", "Straight Leader", "LEADER", "A straight leader with an arrowhead, then text", "_ms_straight_leader"),
                B("qpost1", "Quick Posts - Set 1", "QPOST", "Insert a monument / post block at picked points (choose from the drawing's blocks)", "(ms_block_dialog \"custom_1\")"),
                B("qpost2", "Quick Posts - Set 2", "QPOST", "Insert a monument / post block at picked points (choose from the drawing's blocks)", "(ms_block_dialog \"custom_2\")"),
                B("block_line", "Draw Line of Blocks", "BLOCKLINE", "A row of blocks between two points at a spacing (fence posts, trees)", "_line_of_blocks"),
                B("line_table", "Add Lines to Table", "LINETABLE", "Tag the selected lines L1, L2... and draw a line table", "_line_table"),
                B("curve_table", "Add Curves to Table", "CURVETABLE", "Tag the selected arcs C1, C2... and draw a curve table", "_curve_table"),
                B("tie_table", "Add Multi-ties to Table", "TIETABLE", "Tag the selected ties T1, T2... and draw a tie table", "_multities")),
            Bar("textedit", "FD Text Edit", 2, true,
                B("leroy050", "Leroy 50 (1.25 mm)", "LEROY 050", "Text size Leroy 50: resizes the selected text, or sets the size for new text"),
                B("leroy060", "Leroy 60 (1.5 mm)", "LEROY 060", "Text size Leroy 60"),
                B("leroy080", "Leroy 80 (2.0 mm)", "LEROY 080", "Text size Leroy 80"),
                B("leroy100", "Leroy 100 (2.5 mm)", "LEROY 100", "Text size Leroy 100"),
                B("leroy120", "Leroy 120 (3.0 mm)", "LEROY 120", "Text size Leroy 120"),
                B("leroy140", "Leroy 140 (3.5 mm)", "LEROY 140", "Text size Leroy 140"),
                B("leroy175", "Leroy 175 (4.4 mm)", "LEROY 175", "Text size Leroy 175"),
                B("leroy200", "Leroy 200 (5 mm)", "LEROY 200", "Text size Leroy 200"),
                B("leroy240", "Leroy 240 (6 mm)", "LEROY 240", "Text size Leroy 240"),
                B("text_style", "Fonts / Style", "STYLE", "List the text styles; apply one to the selection or to new text", "_Style"),
                B("arrows_pts", "Arrows Between Points", "ARROWS", "A line with arrowheads at both ends between two picked points", "_ms_arrows"),
                B("scale_one", "Scale One Text Size to Another", "SCALEONE", "Make the selected text the same height as a text you pick", "_scaleone"),
                B("scale_factor", "Scale Selected Text by Factor", "SCALETXT", "Multiply the selected text heights by a factor", "_scaletxt"),
                B("rot180", "Rotate Text 180°", "ROTEXT", "Turn the selected text end for end in place", "_ms_rotext"),
                B("rot_line", "Rotate Text to Match Line", "ROTOLINE", "Rotate the selected text to read along a line or text you pick", "_ms_rotoline"),
                B("slide_text", "Slide Text Along Axis", "SLIDETEXT", "Pick a text, then where it goes - it moves only along its own baseline", "_slidetext"),
                B("text_edit", "Edit Text", "TEXTEDIT", "Pick a text and type its new content", "_TEXTEDIT")),

            // ---------------------------------------------------------------- band 2
            Bar("dimensioning", "Dimensioning", 3, true,
                B("qdim", "Quick Dimension", "QDIM", "Aligned dimensions for every course of the selected lines/polylines, offset to the side you pick", "_QDIM"),
                B("dim_linear", "Linear", "DIMLIN", "Horizontal or vertical dimension (DIMLIN)", "_DIMLINEAR"),
                Sep,
                B("dim_aligned", "Aligned", "DIM", "Dimension parallel to the two points (DIM)", "_DIMALIGNED"),
                B("dim_angular", "Angular", "DIMANG", "Angle dimension: vertex, a point on each leg, arc location", "_DIMANGULAR"),
                B("leader", "Leader", "LEADER", "Leader with text", "_DIMLEADER"),
                B("leader", "Multileader", "LEADER", "Leader with text (FD-Draft draws a leader and text)", "_MLEADER"),
                B("dim_rotated", "Rotated", "DIMLIN", "Linear dimension at an angle: pick the points, then type the angle before placing it", "_DIMLINEAR;_ROTATED"),
                NA("dim_arc", "Arc Length", "_DIMARC", "arc-length dimensions aren't in FD-Draft yet - Label on Curve gives the arc length"),
                Sep,
                B("dim_baseline", "Baseline", "DIMBASELINE", "More dimensions from the first point of the last dimension, stacked outward", "_DIMBASELINE"),
                B("dim_continue", "Continue", "DIMCONTINUE", "More dimensions end to end from the last dimension, on the same line", "_DIMCONTINUE"),
                NA("dim_ordinate", "Ordinate", "_DIMORDINATE", "ordinate dimensions aren't in FD-Draft yet - ID gives a point's N/E"),
                Sep,
                NA("tolerance", "Tolerance", "_TOLERANCE", "geometric tolerance frames are a machine-drawing tool, not used on survey plans"),
                Sep,
                B("center_mark", "Center Mark", "CENTERMARK", "A centre cross on the arcs/circles you pick", "_CENTERMARK"),
                B("center_line", "Center Line", "CENTERLINE", "The centre line between two lines you pick", "_CENTERLINE"),
                Sep,
                NA("oblique", "Make Oblique", "_DIMEDIT;_OBLIQUE", "oblique extension lines aren't supported by FD-Draft's dimensions"),
                Sep,
                B("dim_text_edit", "Edit Dimension Text", "DIMTEXT", "Replace a dimension's text (<> stands for the measurement)", "_DIMEDIT;_NEW"),
                B("dim_text_rotate", "Rotate Dimension Text", "DIMROTATE", "Turn a dimension's text to an angle", "_DIMEDIT;_ROTATE"),
                B("dim_text_move", "Reposition Dimension Text", "DIMTEDIT", "Pick a dimension, then where its text goes", "_DIMTEDIT"),
                B("dim_text_home", "Restore Text Position", "DIMHOME", "Put a dimension's text back in its normal place", "_DIMEDIT;_HOME"),
                NA("dim_inspect", "Inspection Dimension", "_DIMINSPECT", "inspection frames are a machine-drawing tool"),
                NA("reassoc", "Reassociate Dimension", "_DIMREASSOCIATE", "FD-Draft's dimensions aren't tied to geometry - move them with MOVE/STRETCH"),
                NA("disassoc", "Disassociate Dimension", "_DIMDISASSOCIATE", "FD-Draft's dimensions are never associative"),
                NA("dim_jog", "Dimension Jog Line", "_DIMJOGLINE", "jogged dimension lines aren't in FD-Draft yet"),
                NA("dim_space", "Adjust Spacing", "_DIMSPACE", "not in FD-Draft yet - DIMBASELINE spaces stacked dimensions evenly as it draws them"),
                NA("dim_break", "Dimension Break", "_DIMBREAK", "dimension breaks aren't in FD-Draft yet"),
                Sep,
                B("dimstyle", "Dimension Styles", "DIMSTYLE", "List the drawing's dimension styles and choose the one new dimensions use", "_DIMSTYLE"),
                NA("dimstyle_save", "Save Style", "_-DIMSTYLE;_SAVE", "FD-Draft has no dimension style editor - styles come from the firm template"),
                B("dimstyle_restore", "Restore Style", "DIMSTYLE", "Choose the dimension style new dimensions use", "_-DIMSTYLE;_RESTORE"),
                B("dim_status", "Dimension Variable Status", "DIMSTATUS", "List the current dimension style's settings", "_-DIMSTYLE;_STATUS"),
                B("dim_update", "Update", "DIMUPDATE", "Apply the current dimension style to the selected dimensions", "_-DIMSTYLE;_APPLY")),
            Bar("text", "Text", 2, true,
                B("text", "Text", "TEXT", "A single line of text", "_DTEXT"),
                B("mtext", "Multiline Text", "MTEXT", "A block of text over several lines (type \\P or | between lines)", "_MTEXT"),
                B("txt2mtxt", "Text to Multiline Text", "TXT2MTXT", "Combine the selected single-line texts into one multiline text", "_TXT2MTXT"),
                B("text_edit", "Edit Text", "TEXTEDIT", "Pick a text and type its new content", "_TEXTEDIT"),
                B("explore_styles", "Text Styles", "STYLE", "List the drawing's text styles and choose one", "_EXPFONTS")),
            Bar("fdlayer", "FD Layer", 2, true,
                B("lay_copy", "Copy", "LAYCOPY", "Copy the selection onto the layer of an entity you pick", "(def:laycopy)"),
                B("lay_erase", "Erase", "LAYDEL", "Erase everything on the layer of an entity you pick, and the layer itself", "_erase_layer"),
                B("layer_freeze", "Freeze", "LAYFRZ", "Freeze the layer of each entity you pick", "(def:layfrz)"),
                B("lay_thaw", "Thaw", "LAYTHW", "Thaw every frozen layer", "(def:laythw)"),
                B("lay_move", "Move", "LAYMCH", "Move the selection onto the layer of an entity you pick", "(def:laymove)"),
                B("lay_set", "Set", "LAYMCUR", "Make the layer of an entity you pick the current layer", "(def:layset)"),
                B("lay_what", "What", "LAYWHAT", "Report the layer of each entity you pick", "(def:laywhat)"),
                B("layer_iso", "Isolate", "LAYISO", "Hide every layer except those of the selection (or of an entity you pick)", "_ISO"),
                B("lay_uniso", "UnIsolate", "LAYUNISO", "Bring back the layers the last isolate hid", "_UNISO"),
                NA("lay_group", "Group", "(def:laygroup)", "MSCAD's named layer groups aren't in FD-Draft - use Layer States"),
                B("lay_state", "Layer States", "LAYERSTATE", "Save and restore which layers are on, frozen and locked", "_layerstate")),

            // ---------------------------------------------------------------- band 3
            Bar("layer_tools", "Layer Tools", 4, true,
                B("lay_explore", "Explore Layers", "LAYERS", "Show the Layers panel", "_EXPLAYERS"),
                B("lay_state", "Layer States Manager", "LAYERSTATE", "Save and restore layer states", "_LAYERSTATE"),
                B("lay_set", "Set Layer by Entity", "LAYMCUR", "Make the layer of an entity you pick the current layer", "_LAYMCUR"),
                B("set_bylayer", "Set to ByLayer", "SETBYLAYER", "Colour, linetype and lineweight of the selection back to ByLayer", "_SETBYLAYER"),
                Sep,
                B("lay_move", "Layer Match", "LAYMCH", "Move the selection onto the layer of an entity you pick", "_LAYMCH"),
                B("lay_cur", "Change to Current Layer", "LAYCUR", "Put the selection on the current layer", "_LAYCUR"),
                Sep,
                B("layer_iso", "Layer Isolate", "LAYISO", "Hide every layer except those of the selection", "_LAYISO"),
                B("lay_uniso", "Layer Unisolate", "LAYUNISO", "Bring back the layers the last isolate hid", "_LAYUNISO"),
                Sep,
                B("lay_off", "Layer Off", "LAYOFF", "Turn off the layer of each entity you pick", "_LAYOFF"),
                B("lay_on", "Turn All Layers On", "LAYON", "Turn every layer on", "_LAYON"),
                Sep,
                B("layer_freeze", "Layer Freeze", "LAYFRZ", "Freeze the layer of each entity you pick", "_LAYFRZ"),
                B("lay_thaw", "Thaw All Layers", "LAYTHW", "Thaw every frozen layer", "_LAYTHW"),
                Sep,
                B("lay_lock", "Layer Lock", "LAYLCK", "Lock the layer of each entity you pick - its entities can't be selected", "_LAYLCK"),
                B("lay_unlock", "Layer Unlock", "LAYULK", "Unlock the layer of each entity you pick", "_LAYULK"),
                Sep,
                B("lay_erase", "Layer Delete", "LAYDEL", "Erase everything on the layer of an entity you pick, and the layer itself", "_LAYDEL"),
                Sep,
                B("lay_state_save", "Save Layer State", "LAYERSTATE SAVE", "Save the current layer state under a name", "_LAYERSTATESAVE"),
                NA("lay_fade", "Locked Layer Fade", "_SETVAR;LAYLOCKFADECTL", "FD-Draft shows locked layers normally"),
                B("lay_prev", "Layer Previous", "LAYERP", "Undo the last layer on/off/freeze/lock/isolate change", "_LAYERP")),
            Bar("main", "FD Main Control", 5, true,
                NA("assistant", "Assistant", "_Assistant", "MSCAD's Assistant has no FD-Draft equivalent - type HELP for commands"),
                B("project", "Project Manager", "DRAFT", "Choose and draft an FD-Pro job", "_project_manager"),
                B("config", "General Configurations", "CONFIG", "Open the firm standards file (text heights, layers, label rules)", "_cnf_edit_general"),
                NA("toggles", "Toggles/Preference", "_cnf_edit_toggles", "FD-Draft's settings are the firm standards file (CONFIG) and the snap toolbar"),
                NA("hot_toggles", "Hot Toggles", "_tog", "use the Object Snap toolbar and F3"),
                B("info", "Line / Curve / Text Information", "INFO", "Pick a line, arc or text and get its bearing, distance, curve data or content", "_info"),
                NA("grips", "Turn Grips On or Off", "_Tog_Grips", "FD-Draft edits vertices with STRETCH and the Properties panel instead of grips"),
                B("cogo", "COGO", "LINE", "Bearing and distance traverse (LINE): type N45-30-00E 125.50 legs", "_MS_COGO"),
                B("automap", "AutoMap Library", "CODES", "Show the FD-Pro code library (Codes panel)", "_automap_editor"),
                B("store_points", "Store and Edit Points", "POINTS", "Show the Points panel", "_MS_EDITP"),
                B("coord_editor", "Active Coordinate Editor", "POINTS", "Show the Points panel", "_coordedit"),
                NA("traverse_editor", "Active Traverse Editor", "_ms_edit_raw_data", NoTraverse),
                B("rescale", "Re-scale Complete Drawing", "VPSCALE", "Change the sheet's plot scale, resizing labels to suit (VPSCALE)", "_ms_rescale"),
                B("add_points", "Add Points to Objects", "ADDPOINTS", "Put a point object at every vertex of the selected linework", "_MS_AUTOP"),
                B("label_defaults", "Labeling Defaults", "CONFIG", "Open the firm standards file (label heights, layers, bearing format)", "_cnf_edit_bearings"),
                B("view_log", "View Log File", "LOGFILE", "Open this session's command history as a text file", "_mseditor"),
                NA("scale_z", "Scale Z Value", "_SCALEZ", "FD-Draft draws in 2D"),
                B("calculator", "RPN Calculator", "CALC", "Open the Windows calculator", "_calc")),
            Bar("calcs", "FD Calcs", 4, true,
                B("cogo", "COGO", "LINE", "Bearing and distance traverse (LINE)", "_MS_COGO"),
                B("pts_on_obj", "Compute Points on an Object", "PTSONOBJ", "Points along a line or arc: a number of equal parts, or every so far", "_ms_comp_points_on_line_curve"),
                B("turned_angle", "Compute Points by Turned Angle", "TURNANGLE", "From an occupied point and backsight, turn an angle and measure a distance", "_ms_comp_points_turned_angle"),
                B("sta_off", "Compute Points by Station / Offset", "STAOFF", "Point at a station along a line and an offset left or right of it", "_ms_comp_points_stn_offset"),
                B("dot2dot", "Lines by Inversing (dot to dot)", "LINE", "Lines from point to point, inversing each leg (LINE with Node snap)", "_connect_points"),
                B("tan_line", "Line Tangent to Curve", "TANLINE", "A line from a picked point tangent to an arc or circle", "_ms_line_tangent_curve"),
                B("connect_desc", "Connect by Description", "JOINDESC", "Join the job's points with a given code, in point-number order", "_joindesc"),
                B("bestfit_line", "Best Fit a Line", "BESTLINE", "Least-squares line through the selected points", "_LR"),
                B("curve_calc", "COGO Curve Calculations", "CURVECALC", "Solve a curve from any two of R, L, Δ, C, T, E, M", "_MS_CURVE"),
                B("bestfit_curve", "Best Fit a Curve", "BESTCURVE", "Least-squares arc through the selected points", "_LSC"),
                B("curve_solver", "Curve Problem Solver", "CURVECALC", "Solve a curve from any two of R, L, Δ, C, T, E, M", "curvcalc"),
                NA("row", "Right of Way Design", "_right_of_way", "road right-of-way design is outside FD-Draft's drafting scope"),
                B("curve_one_tan", "Curve Off One Tangent", "CURVETAN", "An arc continuing tangent from the end of a line: radius and length", "_arc_on_tangent"),
                B("curve_two_tan", "Curve On Two Tangents", "FILLET", "An arc of a given radius tangent to two lines (FILLET)", "_arc_on_two_tangents"),
                NA("reverse_curve", "Reverse or Compound Curve", "_reverse_curve", "not in FD-Draft yet - chain CURVETAN from the end of each arc")),

            // ---------------------------------------------------------------- band 4
            Bar("coordinate", "FD Coordinate", 5, true,
                B("ascii_out", "Export ASCII Points", "PTEXPORT", "Write the job's points (or the selected point objects) to a P,N,E,Z,D file", "_ascii_out"),
                B("ascii_in", "Import ASCII Points", "PTIMPORT", "Read a P,N,E,Z,D file and draw its points with their numbers", "_ascii_in"),
                B("add_points", "Add Points to Objects", "ADDPOINTS", "Put a point object at every vertex of the selected linework", "_MS_AUTOP"),
                B("lay_move", "Change Point & Object Layer", "LAYMCH", "Move the selection onto the layer of an entity you pick", "_layer_move"),
                B("del_points", "Delete Points from Job", "DELPOINTS", "Erase the selected point objects and the labels that belong to them", "_MS_DELETEP"),
                B("id", "ID North/East of Point", "ID", "Coordinates of a picked point (ID)", "(load \"id\")"),
                B("list_points", "List Points", "LISTP", "List the job's points (all, or a range like 100-150)", "_MS_LISTP"),
                B("update_db", "Update Drawing from Database", "REGEN", "Redraw from the drawing (REGEN)", "_MS_REFRESH"),
                NA("renumber", "Renumber Points", "_ms_start_renumber", NoPointDb),
                B("rotate_pts", "Rotate Points", "ROTATE", "Rotate the selection about a point (ROTATE)", "_MS_ROTATEP"),
                B("shift_pts", "Shift Points", "MOVE", "Move the selection (MOVE)", "_MS_SHIFTP"),
                B("scale_pts", "Scale Points", "SCALEP", "Scale the selection about a base point by a factor", "_MS_SCALEP"),
                NA("stakeout", "Stake Out Points", "_angles_right_report", "stakeout reports come from FD-Pro"),
                B("store_points", "Store and Edit Points", "POINTS", "Show the Points panel", "_MS_EDITP"),
                NA("transfer", "Transfer Points between Jobs", "_ms_start_transfer", NoPointDb),
                B("zoom_point", "Zoom to a Point", "ZOOMP", "Type a point number to zoom to it", "_MS_ZOOMP"),
                B("graphic_editor", "Graphic Coordinate Editor", "POINTS", "Show the Points panel", "_graphic_editor"),
                NA("helmert", "Helmert's Transformation", "_ms_helmert", "coordinate transformations are done in FD-Pro")),
        };

        /// <summary>Every distinct FD-Draft command verb the toolbars run (first word of each command).</summary>
        public static IEnumerable<string> CommandVerbs =>
            All.SelectMany(b => b.Buttons).Where(b => b.Kind == ToolButtonKind.Command && b.Command != null)
                .Select(b => b.Command!.Split(' ')[0]).Distinct();
    }
}

namespace FdDraft.View.Toolbars
{
    /// <summary>Fits the toolbars into as few rows as the window's width allows.</summary>
    public static class ToolbarLayout
    {
        /// <summary>
        /// A bar's width in device-independent pixels at an icon size: each button is the icon
        /// plus its padding and border, separators a few pixels, the layer list its fixed width,
        /// plus the bar's grip and overflow chevron.
        /// </summary>
        public static double EstimateWidth(ToolbarDef bar, int iconSize)
        {
            double w = 24; // grip + overflow button + margins
            foreach (var b in bar.Buttons)
                w += b.Kind == ToolButtonKind.Separator ? 7 : b.Kind == ToolButtonKind.Custom ? 215 : iconSize + ButtonChrome(iconSize);
            return w;
        }

        /// <summary>Padding + border + margin around an icon, both sides together.</summary>
        public static double ButtonChrome(int iconSize) => 2 * Padding(iconSize) + 2 + 2;

        /// <summary>The padding inside a button round its icon.</summary>
        public static double Padding(int iconSize) => iconSize >= 24 ? 2 : 1;

        /// <summary>
        /// Rows for the bars: in catalog order, each bar goes on the first row that still has room
        /// for it in <paramref name="available"/> (a new row when none has), so short bars fill gaps
        /// instead of leaving a row nearly empty. Returns key -> (row, place in row).
        /// </summary>
        public static Dictionary<string, (int Band, int Index)> Pack(IEnumerable<ToolbarDef> bars, int iconSize, double available)
        {
            var result = new Dictionary<string, (int, int)>();
            var used = new List<double>();
            var count = new List<int>();
            foreach (var bar in bars)
            {
                double w = EstimateWidth(bar, iconSize);
                int band = used.FindIndex(u => u + w <= available);
                if (band < 0) { band = used.Count; used.Add(0); count.Add(0); }
                result[bar.Key] = (band, count[band]++);
                used[band] += w;
            }
            return result;
        }
    }
}
