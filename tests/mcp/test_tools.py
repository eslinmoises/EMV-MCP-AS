import re
import unittest
from pathlib import Path

from src.mcp_server.client.ipc_client import AdvanceSteelIpcClient
from src.mcp_server.tools import diagnostic_tools, modeling_tools, scripting_tools
from tests.mocks.mock_as_plugin import MockAdvanceSteelServer


class TestMcpTools(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = MockAdvanceSteelServer(host="127.0.0.1", port=5055)
        cls.server.start()
        cls.client = AdvanceSteelIpcClient(base_url=f"http://127.0.0.1:{cls.server.port}")

    @classmethod
    def tearDownClass(cls):
        cls.server.stop()

    def test_get_active_model_info(self):
        res = diagnostic_tools.get_active_model_info(self.client)
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["as_version"], "2026")
        self.assertEqual(res["data"]["units"], "Metric")

    def test_get_selected_elements(self):
        res = diagnostic_tools.get_selected_elements(self.client)
        self.assertTrue(res["success"])
        self.assertEqual(len(res["data"]), 1)
        self.assertEqual(res["data"][0]["handle"], "1B2C")
        self.assertEqual(res["data"][0]["model_role"], "Column")

    def test_verify_welds_and_assemblies(self):
        res = diagnostic_tools.verify_welds_and_assemblies(self.client)
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["total_workshop_welds"], 1)
        self.assertEqual(res["data"]["total_site_welds"], 1)
        # Verify workshop weld marks same assembly
        welds = res["data"]["welds"]
        self.assertTrue(welds[0]["is_same_assembly"])
        self.assertFalse(welds[1]["is_same_assembly"])

    def test_inspect_main_part(self):
        res = diagnostic_tools.inspect_main_part(self.client, "C1")
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["assembly_mark"], "C1")
        self.assertEqual(res["data"]["main_part_role"], "Column")
        self.assertTrue(res["data"]["is_valid_main_part"])

    def test_get_ucs_and_grids(self):
        res = diagnostic_tools.get_ucs_and_grids(self.client)
        self.assertTrue(res["success"])
        self.assertIn("active_ucs", res["data"])
        self.assertEqual(len(res["data"]["grid_axes"]), 4)

    def test_get_ucs_and_grids_payload_schema(self):
        """Pins the keys SpatialCommandHandler must emit for spatial/ucs-grids.

        An agent placing geometry needs the UCS axes to know whether its coordinates are
        WCS or UCS (rules/advance-steel-modeling.md §1), so a handler that drops one of
        these keys is a silent correctness bug. Extra keys are allowed; missing ones are not.
        """
        data = diagnostic_tools.get_ucs_and_grids(self.client)["data"]

        for axis_key in ("origin", "x_axis", "y_axis", "z_axis"):
            self.assertIn(axis_key, data["active_ucs"])
            self.assertEqual(len(data["active_ucs"][axis_key]), 3)

        self.assertTrue(data["grid_axes"], "a model with grids must report its axes")
        for axis in data["grid_axes"]:
            self.assertIn("name", axis)
            self.assertEqual(len(axis["start"]), 3)
            self.assertEqual(len(axis["end"]), 3)

        self.assertTrue(data["levels"], "at least the ±0.00 model datum must be reported")
        for level in data["levels"]:
            self.assertIn("name", level)
            self.assertIsInstance(level["elevation"], (int, float))

    def test_capture_viewport(self):
        res = diagnostic_tools.capture_viewport(self.client)
        self.assertTrue(res["success"])
        self.assertIn("image_base64", res["data"])

    def test_create_straight_beam(self):
        res = modeling_tools.create_straight_beam(
            self.client,
            start_point=[0, 0, 0],
            end_point=[0, 0, 4000],
            section_name="HEA240",
            material="S275JR",
            model_role="Column",
        )
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["handle"], "BEAM_101")
        self.assertEqual(res["data"]["section_name"], "HEA240")

    def test_create_poly_beam(self):
        pts = [[0.0, 0.0, 0.0], [1000.0, 0.0, 0.0], [2000.0, 1000.0, 0.0]]
        res = modeling_tools.create_poly_beam(self.client, pts, "HEA200")
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["handle"], "PBEAM_601")
        self.assertEqual(res["data"]["vertex_count"], 3)

    def test_create_portal_frame(self):
        res = modeling_tools.create_portal_frame(
            self.client,
            span_mm=12000.0,
            eave_height_mm=6000.0,
            ridge_height_mm=7500.0,
            column_section="HEA 300",
            rafter_section="IPE 300",
        )
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["column_left_handle"], "COL_L_01")
        self.assertEqual(res["data"]["rafter_right_handle"], "RAF_R_02")
        self.assertEqual(len(res["data"]["base_plate_handles"]), 2)
        self.assertEqual(res["data"]["geometry"]["span_mm"], 12000.0)

    def test_apply_detailing_repairs(self):
        res = diagnostic_tools.apply_detailing_repairs(
            self.client,
            repair_actions=["assign_orphaned_plates", "infer_missing_roles"],
            dry_run=True,
        )
        self.assertTrue(res["success"])
        self.assertTrue(res["data"]["dry_run"])
        self.assertEqual(res["data"]["count"], 2)
        self.assertEqual(len(res["data"]["repairs_applied"]), 2)

    def test_create_plate(self):
        points = [[0, 0, 0], [400, 0, 0], [400, 400, 0], [0, 400, 0]]
        res = modeling_tools.create_plate(self.client, contour_points=points, thickness=25.0)
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["handle"], "PLATE_202")
        self.assertEqual(res["data"]["thickness_mm"], 25.0)

    def test_set_main_part(self):
        res = modeling_tools.set_main_part(self.client, assembly_handle="ASS_01", new_main_part_handle="BEAM_101")
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["main_part_handle"], "BEAM_101")

    def test_execute_csharp_script(self):
        script = "var beam = new StraightBeam();"
        res = scripting_tools.execute_csharp_script(self.client, script)
        self.assertTrue(res["success"])
        self.assertIn("Roslyn", res["data"]["output"])

    def test_create_standard_joint(self):
        res = modeling_tools.create_standard_joint(
            self.client,
            primary_handle="BEAM_101",
            joint_type="BasePlate",
            secondary_handles=["BEAM_102"],
        )
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["handle"], "JOINT_303")
        self.assertEqual(res["data"]["joint_type"], "BasePlate")

    def test_apply_beam_cut_or_notch(self):
        res = modeling_tools.apply_beam_cut_or_notch(
            self.client,
            beam_handle="BEAM_101",
            cut_type="shortening",
            cut_length_mm=100.0,
        )
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["handle"], "CUT_404")
        self.assertEqual(res["data"]["cut_type"], "shortening")

    def test_modify_element_properties(self):
        res = modeling_tools.modify_element_properties(
            self.client,
            handle="BEAM_101",
            material="S355JR",
            model_role="Rafter",
        )
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["handle"], "BEAM_101")
        self.assertTrue(res["data"]["success"])


    def test_audit_assembly_integrity(self):
        res = diagnostic_tools.audit_assembly_integrity(self.client)
        self.assertTrue(res["success"])
        self.assertIn("orphaned_parts", res["data"])

    def test_detect_clashes_and_clearances(self):
        res = diagnostic_tools.detect_clashes_and_clearances(self.client)
        self.assertTrue(res["success"])
        self.assertIn("clashes", res["data"])
        self.assertEqual(res["data"]["method"], "AABB_SweepAndPrune")

    def test_query_elements_in_box(self):
        res = diagnostic_tools.query_elements_in_box(
            self.client,
            min_point=[-100.0, -100.0, -100.0],
            max_point=[500.0, 500.0, 1000.0],
            element_types=["StraightBeam"],
        )
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["count"], 1)
        self.assertEqual(res["data"]["elements"][0]["handle"], "1B2C")
        self.assertEqual(res["data"]["box"]["min_point"], [-100.0, -100.0, -100.0])

    def test_create_bolt_pattern(self):
        res = modeling_tools.create_bolt_pattern(
            self.client,
            connected_handles=["1B2C", "2D3E"],
            origin=[150.0, 150.0, 400.0],
            normal=[0.0, 0.0, 1.0],
            bolt_standard="DIN 931",
            bolt_grade="8.8",
            bolt_diameter_mm=20.0,
            nx=2,
            ny=2,
            dx=70.0,
            dy=70.0,
            is_site_bolt=True,
        )
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["handle"], "BOLT_501")
        self.assertEqual(res["data"]["count"], 4)
        self.assertTrue(res["data"]["is_site_bolt"])

    def test_create_poly_beam(self):
        res = modeling_tools.create_poly_beam(
            self.client,
            points=[[0.0, 0.0, 0.0], [1000.0, 500.0, 0.0], [2000.0, 0.0, 0.0]],
            section_name="HEA200",
            material="S275JR",
            model_role="Beam",
        )
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["handle"], "PBEAM_601")
        self.assertEqual(res["data"]["vertex_count"], 3)

    def test_query_elements(self):
        res = diagnostic_tools.query_elements(self.client, model_role="Column")
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["count"], 1)
        self.assertEqual(res["data"]["elements"][0]["role"], "Column")
        self.assertEqual(res["data"]["elements"][0]["handle"], "1B2C")

    def test_get_supported_joints_catalog(self):
        res = diagnostic_tools.get_supported_joints_catalog(self.client)
        self.assertTrue(res["success"])
        self.assertEqual(res["data"]["total_count"], 4)
        joint_types = [j["joint_type"] for j in res["data"]["joints"]]
        self.assertIn("BasePlate", joint_types)
        self.assertIn("ClipAngle", joint_types)
        self.assertIn("EndPlate", joint_types)

    def test_validate_section(self):
        res_valid = diagnostic_tools.validate_section(self.client, "HEB300")
        self.assertTrue(res_valid["success"])
        self.assertTrue(res_valid["data"]["is_valid"])

        res_invalid = diagnostic_tools.validate_section(self.client, "NON_EXISTENT_PROFILE_XYZ")
        self.assertTrue(res_invalid["success"])
        self.assertFalse(res_invalid["data"]["is_valid"])


REPO_ROOT = Path(__file__).resolve().parents[2]
PLUGIN_COMMANDS = REPO_ROOT / "src" / "as_plugin" / "Commands"
DISPATCHER = PLUGIN_COMMANDS / "CommandDispatcher.cs"


class TestDispatcherRouting(unittest.TestCase):
    """Guards the two-place route registration in CommandDispatcher.cs.

    A route has to be listed twice: once in ``KnownRoutes`` (the gate that answers
    ENDPOINT_NOT_FOUND before any transaction is opened) and once in the ``Route`` switch
    (which picks the handler). Registering it in only one of them is silent — the endpoint
    either 404s despite having a handler, or falls through the switch to a second 404 after
    a transaction was already opened. No AutoCAD is needed to catch that, so it is checked
    here rather than left to a manual smoke test on a workstation with Advance Steel.
    """

    EXPECTED_ROUTES = {
        "elements/joint": "JointCommandHandler",
        "elements/cut": "FeatureCommandHandler",
        "elements/modify": "ModifyCommandHandler",
        # CONTRACT-001/002 routes, kept here so a refactor cannot quietly drop them.
        "elements/beam": "BeamCommandHandler",
        "elements/plate": "PlateCommandHandler",
        "elements/bolt": "BoltCommandHandler",
        "elements/poly-beam": "PolyBeamCommandHandler",
        "spatial/ucs-grids": "SpatialCommandHandler",
        "spatial/box": "SpatialCommandHandler",
        "audit/assembly-integrity": "AuditCommandHandler",
        "audit/clashes": "AuditCommandHandler",
        # CONTRACT-004A command-mode production routes
        "production/numbering": "ProductionCommandHandler",
        "production/export-nc": "ProductionCommandHandler",
        "production/drawing-status": "ProductionCommandHandler",
        # CONTRACT-006 query and catalog routes
        "elements/query": "QueryCommandHandler",
        "elements/joints-catalog": "QueryCommandHandler",
        "elements/validate-section": "QueryCommandHandler",
        # CONTRACT-007 production BOM route
        "production/bom": "BomCommandHandler",
        # CONTRACT-008 generative portal frame and doctor
        "elements/portal-frame": "PortalFrameCommandHandler",
        "audit/repair": "DoctorCommandHandler",
    }

    COMMAND_ROUTES = {
        "production/numbering",
        "production/export-nc",
        "production/drawing-status",
    }

    @classmethod
    def setUpClass(cls):
        cls.source = DISPATCHER.read_text(encoding="utf-8")
        known_block = re.search(
            r"KnownRoutes\s*=\s*new\((?:.|\n)*?\{((?:.|\n)*?)\};", cls.source
        )
        assert known_block, "could not locate the KnownRoutes initializer"
        cls.known_routes = set(re.findall(r'"([^"]+)"', known_block.group(1)))

        cmd_block = re.search(
            r"CommandRoutes\s*=\s*new\((?:.|\n)*?\{((?:.|\n)*?)\};", cls.source
        )
        assert cmd_block, "could not locate the CommandRoutes initializer"
        cls.command_routes = set(re.findall(r'"([^"]+)"', cmd_block.group(1)))

    def test_routes_are_gated_by_known_routes(self):
        for route in self.EXPECTED_ROUTES:
            self.assertIn(
                route,
                self.known_routes,
                f"{route} is missing from KnownRoutes and would 404 before reaching its handler",
            )

    def test_routes_reach_their_handler(self):
        for route, handler in self.EXPECTED_ROUTES.items():
            self.assertRegex(
                self.source,
                rf'"{re.escape(route)}"\s*=>\s*{handler}\.',
                f"{route} is not dispatched to {handler}",
            )

    def test_command_routes_are_registered_and_isolated(self):
        for route in self.COMMAND_ROUTES:
            self.assertIn(
                route,
                self.command_routes,
                f"{route} must be registered in CommandRoutes for command-mode execution",
            )

    def test_handlers_exist_with_their_entry_point(self):
        for file_name, entry_point in (
            ("JointCommandHandler.cs", "Create"),
            ("FeatureCommandHandler.cs", "Apply"),
            ("ModifyCommandHandler.cs", "Modify"),
            ("BoltCommandHandler.cs", "Create"),
            ("PolyBeamCommandHandler.cs", "Create"),
            ("ProductionCommandHandler.cs", "RunNumbering"),
            ("ProductionCommandHandler.cs", "ExportNc"),
            ("ProductionCommandHandler.cs", "DrawingStatus"),
            ("QueryCommandHandler.cs", "QueryElements"),
            ("QueryCommandHandler.cs", "GetJointsCatalog"),
            ("QueryCommandHandler.cs", "ValidateSection"),
            ("BomCommandHandler.cs", "GenerateBom"),
            ("PortalFrameCommandHandler.cs", "Create"),
            ("DoctorCommandHandler.cs", "Repair"),
        ):
            path = PLUGIN_COMMANDS / "Handlers" / file_name
            self.assertTrue(path.is_file(), f"{file_name} is missing")
            self.assertRegex(
                path.read_text(encoding="utf-8"),
                rf"public static CommandResult {entry_point}\(CommandContext",
                f"{file_name} does not expose {entry_point}(CommandContext)",
            )

    def test_handlers_do_not_open_their_own_transaction(self):
        """rules/transaction-safety.md §3 & §5: handlers never open their own transaction boundary."""
        for file_name in (
            "JointCommandHandler.cs",
            "FeatureCommandHandler.cs",
            "ModifyCommandHandler.cs",
            "BoltCommandHandler.cs",
            "PolyBeamCommandHandler.cs",
            "ProductionCommandHandler.cs",
            "QueryCommandHandler.cs",
            "BomCommandHandler.cs",
            "PortalFrameCommandHandler.cs",
            "DoctorCommandHandler.cs",
        ):
            source = (PLUGIN_COMMANDS / "Handlers" / file_name).read_text(encoding="utf-8")
            self.assertNotIn("LockDocument()", source, f"{file_name} opens its own document lock")
            self.assertNotIn("StartTransaction(", source, f"{file_name} opens its own transaction")


if __name__ == "__main__":
    unittest.main()
