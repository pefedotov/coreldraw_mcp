using System.Linq;
using Xunit;
using CorelMcp;

namespace CorelMcpAddon.Tests
{
    public class ToolCatalogTests
    {
        private static string[] Names()
        {
            return ToolCatalog.Build().List().Select(t => t.Name).ToArray();
        }

        [Fact]
        public void Build_RegistersEveryToolExactlyOnce()
        {
            var tools = ToolCatalog.Build().List();

            Assert.NotEmpty(tools);
            var names = tools.Select(t => t.Name).ToArray();
            Assert.Equal(names.Length, names.Distinct().Count());
        }

        /// <summary>
        /// The essentials are asserted individually rather than by listing the whole set, so
        /// that adding a tool does not break the test but removing one does.
        /// </summary>
        [Fact]
        public void Build_ExposesTheEssentialTools()
        {
            var names = Names();

            var essential = new[]
            {
                // documents and pages
                "create_document", "open_document", "save_document", "save_document_as",
                "close_document", "get_document_info", "list_documents", "add_page", "get_pages",
                "set_document_unit", "set_page_size", "set_page_orientation", "set_active_page",
                // drawing
                "create_rectangle", "create_ellipse", "create_line", "create_polygon", "create_star",
                "create_text", "get_text", "replace_text", "convert_text_to_curves",
                "import_image", "import_svg",
                // colour, outline, transparency
                "set_fill_color", "set_fill_gradient", "set_fill_none",
                "set_outline_color", "set_outline_width", "set_outline_none",
                "set_transparency", "clear_transparency", "get_shape_color",
                // editing
                "set_shape_position", "set_shape_size", "scale_shape", "move_shape", "rotate_shape",
                "duplicate_shape", "order_shape", "group_shapes", "ungroup_shapes", "align_shapes",
                "delete_shape", "list_shapes", "get_shape_info",
                "select_shapes", "clear_selection", "get_selection",
                "combine_shapes", "break_apart_shapes", "weld_shapes", "trim_shapes", "intersect_shapes",
                "fillet_shape", "chamfer_shape", "scallop_shape",
                // navigation and layout
                "set_zoom", "skew_shape", "set_grid", "add_guide", "list_guides", "delete_guides",
                // extra drawing
                "create_spiral", "create_graph_paper", "create_curve",
                "create_dimension", "create_connector",
                // extra text
                "create_paragraph_text", "fit_text_to_path", "place_text_inside", "convert_text_type",
                // extra fills
                "set_fill_pattern", "set_fill_texture", "set_fill_postscript",
                // effects
                "blend_shapes", "contour_shape", "drop_shadow", "envelope_shape", "extrude_shape",
                "lens_shape", "distort_shape", "adjust_color", "clear_effect",
                "flatten_effects", "separate_shape",
                // object composition
                "convert_to_symbol", "revert_symbol_to_shapes", "place_in_powerclip",
                "extract_powerclip", "clone_shape", "copy_properties", "set_locked",
                "list_objects", "set_object_name",
                // layers
                "create_layer", "list_layers", "set_layer", "set_active_layer", "move_shapes_to_layer",
                // output
                "export_pdf", "export_png", "export_svg", "export_dxf",
                "server_status", "server_log"
            };

            var missing = essential.Where(name => !names.Contains(name)).ToArray();
            Assert.True(missing.Length == 0, "Missing tools: " + string.Join(", ", missing));
        }

        /// <summary>
        /// An argument whose handler reads a JSON array must be declared as one. A "string"
        /// schema makes the tool uncallable from a schema-conforming client, because the
        /// caller has no way to pass an array.
        /// </summary>
        [Fact]
        public void Build_DeclaresArrayArgumentsAsArrays()
        {
            var tools = ToolCatalog.Build().List().ToDictionary(t => t.Name);

            foreach (var (tool, property) in new[]
            {
                ("add_guide", "positions"),
                ("create_curve", "points"),
                ("create_dimension", "points"),
                ("select_shapes", "indices")
            })
            {
                var schema = tools[tool].InputSchema["properties"][property];
                Assert.True(schema != null, tool + "." + property + " is not declared");
                Assert.True(schema["type"] != null && schema["type"].AsString() == "array",
                    tool + "." + property + " must be declared as an array");
            }

            foreach (var property in new[] { "from", "to" })
            {
                var schema = tools["create_connector"].InputSchema["properties"][property];
                Assert.True(schema != null && schema["anyOf"] != null,
                    "create_connector." + property + " must accept a point");
            }
        }

        /// <summary>
        /// The selector must carry the container arguments that make objects on another layer
        /// or inside a group reachable — without them a tool can only ever see the active layer.
        /// </summary>
        [Fact]
        public void Build_SelectorCarriesLayerAndGroup()
        {
            var tools = ToolCatalog.Build().List().ToDictionary(t => t.Name);

            foreach (var name in new[] { "move_shape", "set_fill_color", "list_objects", "set_object_name" })
            {
                var layer = tools[name].InputSchema["properties"]["layer"];
                Assert.True(layer != null, name + " has no 'layer' argument");
                Assert.True(layer["type"].AsString() == "string", name + ".layer must be a string");
            }

            foreach (var name in new[] { "move_shape", "set_fill_color", "list_objects" })
            {
                var group = tools[name].InputSchema["properties"]["group"];
                Assert.True(group != null, name + " has no 'group' argument");
                Assert.True(group["type"].AsString() == "string", name + ".group must be a string");
            }
        }

        [Fact]
        public void Build_EveryToolHasDescriptionAndObjectSchema()
        {
            foreach (var tool in ToolCatalog.Build().List())
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.Description), tool.Name + " has no description");
                Assert.NotNull(tool.InputSchema);
                var type = tool.InputSchema["type"] == null ? "(missing)" : tool.InputSchema["type"].AsString();
                Assert.True(type == "object", tool.Name + " inputSchema type is " + type);
            }
        }
    }
}
