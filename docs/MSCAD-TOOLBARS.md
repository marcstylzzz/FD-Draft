# MSCAD toolbars in Marc's Default workspace

Read from Marc's MSCAD `Default.CUI` (its Default workspace lists the toolbars shown, by row) and `icad.cui`.
FD-Draft's MSCAD-style toolbars are built from this list. Commands are MSCAD's own (LISP/ARX); a flyout is a button that drops down more buttons.

## Screenshot 3 (row 1)

### MS Labels 1

| Button | MSCAD help | Command |
|---|---|---|
| Auto Split Bearing | Split a bearing into deg - min - sec across line | `_split_bearings` |
| Place Bearing on center of line | Place Bearing on center of line | `(bear_dist_label 5)` |
| Auto Bearing off line | Place a line bearing anywhere on drawing | `_ms_bear_off_line` |
| Auto Distance | Place Distance on center of a line | `(bear_dist_label 6)` |
| Auto Distance off line | Auto Distance off line | `_ms_dist_off_line` |
| Auto Bearing/Distance | Place bearing opposite distance on line | `(bear_dist_label 4)` |
| Auto Bearing - Distance | Place bearing before distance on same side of line | `(bear_dist_label 2)` |
| Auto Distance - Bearing | Place distance before bearing on same side of line | `(bear_dist_label 1)` |
| Auto Bearing/Distance//Line | Place bearing above distance on same side of line | `(bear_dist_label 7)` |
| Auto Distance/Bearing//Line | Place distance above bearing on same side of line | `(bear_dist_label 3)` |
| Auto Add Angle | Add Angle between two lines | `_ms_auto_angle` |
| Auto Arrows on Line | Add arrows to line offset equal to labels (many Styles) | `(bear_dist_label 8)` |
| Label ON Curve | Curve information follows arc only | `_ms_man_curve_label_on_arc` |
| Label OFF Curve | Curve information placed anywhere in drawing | `_ms_curve_label_off_arc` |
| Text on ARC | Manually place any text to follow the curve | `_ms_text_on_arc` |

## Screenshot 5 (row 2)

### Zoom

| Button | MSCAD help | Command |
|---|---|---|
| **Redraw** (flyout) | Refreshes the display of the current drawing | |
| ↳ Redraw | Refreshes the display of the current window | `'_REDRAW` |
| ↳ Redraw All | Refreshes the display of all the windows for the current drawing | `'_REDRAWALL` |
| ↳ Regen | Recalculates display coordinates for the current window | `'_REGEN` |
| ↳ Regen All | Recalculates display coordinates for all windows for the current drawing | `'_REGENALL` |
| Real-Time Pan | Real-Time Pan | `'_RTPAN` |
| Real-Time Zoom | Real-Time Zoom | `'_RTZOOM` |
| Zoom Previous | Zooms to the previous view | `'_ZOOM;_P` |
| Zoom All | Zooms All | `'_ZOOM;_A` |
| Zoom Left | Zooms in - you specify lower-left corner | `'_ZOOM;_L` |
| Zoom Center | Zooms in, centered on a point you specify | `'_ZOOM;_C` |
| Zoom Object | Zooms to the extents of selected object | `'_ZOOM;_OB` |
| Zoom Window | Zooms to a window that you specify | `'_ZOOM;_W` |
| Zoom In | Zooms in on the center of the window by a factor of 2 | `'_ZOOM;2x` |
| Zoom Out | Zooms out from the center of the window by a factor of 1/2 | `'_ZOOM;.5x` |
| Zoom Extents | Zooms to the extents of the drawing | `'_ZOOM;_E` |
| Define View | Displays a perspective view from a given point | `_DVIEW` |

### Entity Snaps

| Button | MSCAD help | Command |
|---|---|---|
| Track Point | Sets snap to the Temp Track Point | `_TT` |
| From Point | Specify offset point | `_FROM` |
| Mid Between 2 Points | Sets snap to the midpoint between two points | `_M2P` |
| Set Nearest Snap | Sets snap to the nearest point on an entity | `_NEAREST` |
| Set Endpoint Snap | Sets snap to the end of a linear entity | `_ENDPOINT` |
| Set Midpoint Snap | Sets snap to the midpoint of a linear entity | `_MIDPOINT` |
| Set Center Snap | Sets snap to the center point | `_CENTER` |
| Set Perpendicular Snap | Sets snap perpendicular to selected entity | `_PERPENDICULAR` |
| Set Tangent Snap | Sets snap tangent to selected entity | `_TANGENT` |
| Set Quadrant Snap | Sets snap to the nearest quadrant of a circle | `_QUADRANT` |
| Set Insertion Point Snap | Sets snap to the insertion point of a block | `_INSERTION` |
| Set Point Snap | Sets snap to the nearest Point entity | `_NODE` |
| Set Extension Line Snap | Sets snap to the extension line of entity | `_EXTENSION` |
| Set Parallel line Snap | Sets snap to the parallel line of other entity | `_PARALLEL` |
| Set Intersection Snap | Sets snap to the point in space where entities actually intersect | `_INTERSECTION` |
| Set Apparent Intersection Snap | Sets snap to where it appears entities intersect in the current view | `_APPARENT` |
| Disable Running Entity Snaps | Disable all running entity snaps | `'_OSTOGGLE` |
| Clear Entity Snaps | Turns all entity snaps off | `_NONE` |

### MS Ties

| Button | MSCAD help | Command |
|---|---|---|
| Auto House Tie with Arrows | Draw House ties to lot boundaries with arrrows | `_ms_house_tie_arrows` |
| Auto House Tie | Automatic House Ties - No Arrows | `_ms_house_tie` |
| Manual House Tie with Arrows | Manual House Tie and place arrows | `(ms_man_house_tie_arrows 2)` |
| Manual House Tie | Manual Entry of House Ties - No Arrows | `(ms_man_house_tie_arrows 1)` |
| Leader Scale... | Use this to set the arrow size for the straight and curvey leader | `_set_leader_scale` |
| Curvey Leader | Draw curvey leader with arrowhead | `_ms_curvey_leader` |
| Straight Leader | Draw a straight leader with arrowhead | `_ms_straight_leader` |
| Quick Posts - Set 1 | Custom Posts Icons #1 | `(ms_block_dialog "custom_1")` |
| Quick Posts - Set 2 | Custom Posts Icons #2 | `(ms_block_dialog "custom_2")` |
| Draw Line of Blocks | Draw Line of Blocks | `_line_of_blocks` |
| Add Lines to Table | Generate or Add to a Line Table | `_line_table` |
| Add Curves to Table | Generate or Add to a Curve Table | `_curve_table` |
| Add Multities to Table | This will help you create a table of multities or radial line | `_multities` |

### MS FieldGenius

| Button | MSCAD help | Command |
|---|---|---|
| FieldGenius SyncWizard | Start FieldGenius SyncWizard | `_SyncWizard` |
| Upload to FieldGenius | Upload to FieldGenius | `_ms_sync_up` |
| Download FieldGenius | Download from FieldGenius | `_ms_sync_down` |
| FieldGenius Project | Export FieldGenius Project | `_fg_make_proj` |
| FieldGenius Coordinates | Export FieldGenius Coordinates | `_fg_write_xyz_out` |

## Screenshot 4 (row 3)

### MS Main Control

| Button | MSCAD help | Command |
|---|---|---|
| Assistant | This opens the Assistant dialog | `_Assistant` |
| Project Manager | This will help you manage your MicroSurvey projects | `_project_manager` |
| General Configurations | This displays a configuration dialog for your drawing | `_cnf_edit_general` |
| Toggles/Preference | Displays the system toggles configuration dialog | `_cnf_edit_toggles` |
| Hot Toggles | This will open the Hot Toggles dialog | `_tog` |
| Line / Curve / Text Information | Line / Curve / Text Information | `_info` |
| Turn Grips On or Off | Turn Grips On or Off | `_Tog_Grips` |
| COGO | Command Line COGO | `_MS_COGO` |
| AutoMap Library | AutoMap Editing System | `_automap_editor` |
| Store and Edit Points | Use the Single Point Editor to Manipulate the Coordinate Database | `_MS_EDITP` |
| Active Coordinate Editor | Use the Active Coordinate Editor to Manipulate the Coordinate Database | `_coordedit` |
| Active Traverse Editor | Edit Traverse using Active Drawing Technology | `_ms_edit_raw_data` |
| Re-scale complete drawing | Re-scale complete drawing | `_ms_rescale` |
| Add Points to Objects | Auto Add Points to Objects | `_MS_AUTOP` |
| Labeling Defaults | Labeling Defaults | `_cnf_edit_bearings` |
| View Log File | View Log File | `_mseditor` |
| Scale z value | Scale the Z value of objects | `_SCALEZ` |
| RPN Calculator | RPN Calculator | `_calc` |

### MS Defaults

| Button | MSCAD help | Command |
|---|---|---|
| Main Job Defaults | This displays a configuration dialog for your drawing | `_cnf_edit_general` |
| System Toggles | Displays the system toggles configuration dialog | `_cnf_edit_toggles` |
| Set Azimuth | Set Azimuth | `(setvar "aunits" 1)` |
| Set Quadrants | Set Quadrants | `(setvar "aunits" 4)` |
| Bearing Defaults | Bearing Defaults | `_cnf_edit_bearings` |
| Distance Defaults | Distance Defaults | `_cnf_edit_distances` |
| Elevation Defaults | Elevations configuration dialog | `_cnf_edit_elevations` |
| Description Defaults | Descriptions configuration dialog | `_cnf_edit_descriptions` |
| Point Number Defaults | Distances configuration dialog | `_cnf_edit_pointnumbers` |
| Lot / Area Defaults | Lots configuration dialog | `_cnf_edit_lots` |
| Delete Project | Delete Project Database Files | `_prj_delete_dir` |
| Save as Default configuration | Save current settings as the new defaults | `_ms_save_defaults` |
| Reset to Factory Defaults | Restores the defaults set by software developer. | `_cnf_set_factory_defaults` |
| Save Configuration File | Save the current settings to a config file | `_save_config` |
| Read Configuration File | Read a config file from the disk | `_read_config` |

### Draw

| Button | MSCAD help | Command |
|---|---|---|
| **Line** (flyout) | Draws a line | |
| ↳ Line | Draws a line | `_LINE` |
| ↳ Ray | Draws a line of infinite length in one direction from starting point | `_RAY` |
| ↳ Infinite Line | Draws a line of infinite length in both directions from starting point | `_INFLINE` |
| Polyline | Draws a polyline, including straight and arc segments | `_POLYLINE` |
| Multiline | Draws a Multiline - a defined set of parallel lines | `_MLINE` |
| Spline | Creates a new spline, or modifies an existing polyline | `_SPLINE` |
| Freehand | Draws unconstrained shapes, following your mouse movements | `_FREEHAND` |
| **Center-Radius** (flyout) | Draws a circle given a center point and radius | |
| ↳ Center-Radius | Draws a circle given a center point and radius | `_CIRCLE` |
| ↳ Center-Diameter | Draws a circle given a center point and diameter | `_CIRCLE;\_D;` |
| ↳ 2-Point | Draws a circle given 2 end points of the diameter | `_CIRCLE;_2P;` |
| ↳ 3-Point | Draws a circle given 3 points on the circle | `_CIRCLE;_3P;` |
| ↳ Radius-Tangents | Draws a circle of the given radius, tangent to 2 entities | `_CIRCLE;_RTT;` |
| ↳ Tangent-Tangent-Tangent | Draws a circle, tangent to 3 entities | `_CIRCLE;_TTT;` |
| ↳ Convert Arc to Circle | Turns an existing arc into a complete circle | `_CIRCLE;_A;` |
| **3-Point Arc** (flyout) | Draws an arc through 3 points | |
| ↳ 3-Point | Draws an arc through 3 points | `_ARC;\\\` |
| ↳ Start-Center-End | Draws an arc given start, center, and end | `_ARC;\_C;\\` |
| ↳ Start-Center-Angle | Draws an arc given start, center, and included angle | `_ARC;\_C;\_A;` |
| ↳ Start-Center-Length | Draws an arc given start, center, and chord length | `_ARC;\_C;\_L;` |
| ↳ Start-End-Angle | Draws an arc given start, end, and included angle | `_ARC;\_E;\_A;` |
| ↳ Start-End-Direction | Draws an arc given start, end, and starting direction | `_ARC;\_E;\_D;` |
| ↳ Start-End-Radius | Draws an arc given start, end, and radius | `_ARC;\_E;\_R;` |
| ↳ Start-End-Center | Draws an arc given start, end, and center | `_ARC;\_E;\\` |
| ↳ Center-Start-End | Draws an arc given center, start, and end | `_ARC;_C;\\\` |
| ↳ Center-Start-Angle | Draws an arc given center, start, and included angle | `_ARC;_C;\\_A;` |
| ↳ Center-Start-Length | Draws an arc given center, start, and chord length | `_ARC;_C;\\_L;` |
| ↳ Start-Angle-Center | Draws an arc given start, included angle, and center | `_ARC;\_A;\_C;` |
| ↳ Start-Angle-End | Draws an arc given start, included angle, and end | `_ARC;\_A;\\` |
| ↳ Start-Direction-End | Draws an arc given start, starting direction, and end | `_ARC;\_D;\\` |
| ↳ Start-Radius-End | Draws an arc given start, radius, and end | `_ARC;\_R;\\` |
| ↳ Start-Radius-Angle | Draws an arc given start, radius, and included angle | `_ARC;\_R;\_A;` |
| ↳ Tangent | Draws an arc continuing from the previous entity | `_ARC;;` |
| **Axis-Axis** (flyout) | Draws an ellipse given both axes | |
| ↳ Axis-Axis | Draws an ellipse given both axes | `_ELLIPSE` |
| ↳ Axis-Rotation | Draws an ellipse given major axis and rotation | `_ELLIPSE;\\_R` |
| ↳ Center-Axes | Draws an ellipse given center and axes | `_ELLIPSE;_C;\\\` |
| ↳ Center-Rotation | Draws an ellipse given center and rotation | `_ELLIPSE;_C;\\_R` |
| **Axes-Angles** (flyout) | Draws an elliptical arc given major and minor axes, and angles | |
| ↳ Axis-Axis | Draws an elliptical arc given major and minor axes, and angles | `_ELLIPSE;_A;\\\\\` |
| ↳ Axis-Rotation | Draws an elliptical arc given major axis, rotation, and angles | `_ELLIPSE;_A;\\_R;\\\` |
| ↳ Center-Axes | Draws an elliptical arc given center, axes, and angles | `_ELLIPSE;_A;_C;\\\\\` |
| ↳ Center-Rotation | Draws an elliptical arc given center, rotation, and angles | `_ELLIPSE;_A;_C;\\_R;\\\` |
| Point | Draws a point | `_POINT` |
| Helix | Draws a Helix | `_HELIX` |
| **Polygon** (flyout) | Draws a polygon | |
| ↳ Rectangle | Draws a rectangle | `_RECTANGLE` |
| ↳ Polygon Center-Vertex | Draws a polygon using the center point and a vertex | `_POLYGON;\\_I;\` |
| ↳ Polygon Center-Side | Draws a polygon using center point and distance to side | `_POLYGON;\\_C;\` |
| ↳ Polygon Edge | Draws a polygon using the length and orientation of an edge | `_POLYGON;\_E;\\` |
| Wipeout | Draws a wipeout. | `_WIPEOUT` |
| Revision Cloud | Draws a revision cloud | `_REVCLOUD` |
| Donut | Draws a donut | `_DONUT` |
| Plane | Draws a filled plane | `_SOLID` |
| Traces | Draws lines of a specified width | `_TRACE` |
| Insert Block... | Displays the dialog for inserting existing blocks | `_INSERT` |
| **Hatch** (flyout) |  | |
| ↳ Hatch | Hatches the enclosed area around the selected point | `_HATCH` |
| ↳ Gradient | Fills an enclosed area or selected objects with a gradient fill | `_GRADIENT` |
| ↳ Boundary | Creates regions or polylines from enclosed areas | `_BOUNDARY` |
| ↳ Hatch Selection | Toggles Hatch Selection On/Off | `^^(progn (setvar "PICKSTYLE" (if (= (logand (getvar "PICKSTYLE") 2) 0) (logior (getvar "PI` |
| Text | Creates a single line of text | `_DTEXT` |
| Multiline Text | Creates a multiple-line block of text | `_MTEXT` |

