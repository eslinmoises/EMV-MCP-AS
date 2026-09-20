"""Tests for AISC 358-16 and Standard Connection Recipe Catalog."""

import unittest
from src.mcp_server.catalogs.aisc_connections import (
    build_bfp_recipe,
    build_extended_end_plate_recipe,
    build_shear_tab_recipe,
    get_section_dimensions,
)


class TestAiscCatalog(unittest.TestCase):
    def test_get_section_dimensions_heb_and_ipe(self):
        heb400 = get_section_dimensions("HEB400")
        self.assertEqual(heb400["d"], 400.0)
        self.assertEqual(heb400["bf"], 300.0)

        ipe360 = get_section_dimensions("IPE360")
        self.assertEqual(ipe360["d"], 360.0)
        self.assertEqual(ipe360["bf"], 170.0)

    def test_build_bfp_recipe_generates_all_components(self):
        recipe = build_bfp_recipe("BEAM_01", "COL_01", "IPE360", "HEB400")
        self.assertIn("BFP AISC 358-16", recipe["connection_name"])
        self.assertEqual(len(recipe["plates"]), 7)  # Top, Bot, Shear, 4 Stiffeners
        self.assertEqual(len(recipe["bolt_groups"]), 3)  # Top, Bot, Shear
        self.assertEqual(len(recipe["shop_welds"]), 7)  # All shop welded to column

        # Verify all shop welds bind to the column
        for weld in recipe["shop_welds"]:
            self.assertEqual(weld["main_part_handle"], "COL_01")

    def test_build_extended_end_plate_4e_and_4es(self):
        # 4E unstiffened
        recipe_4e = build_extended_end_plate_recipe("BEAM_01", "COL_01", "IPE360", "HEB400", "4E")
        self.assertEqual(len(recipe_4e["plates"]), 1)
        self.assertEqual(recipe_4e["shop_welds"][0]["main_part_handle"], "BEAM_01")

        # 4ES stiffened
        recipe_4es = build_extended_end_plate_recipe("BEAM_01", "COL_01", "IPE360", "HEB400", "4ES")
        self.assertEqual(len(recipe_4es["plates"]), 2)  # End plate + gusset stiffener
        self.assertEqual(len(recipe_4es["shop_welds"]), 2)
        for w in recipe_4es["shop_welds"]:
            self.assertEqual(w["main_part_handle"], "BEAM_01")

    def test_build_shear_tab_recipe(self):
        recipe = build_shear_tab_recipe("BEAM_01", "COL_01", "IPE360", "HEB400", num_bolts=4)
        self.assertEqual(len(recipe["plates"]), 1)
        self.assertEqual(len(recipe["shop_welds"]), 1)
        self.assertEqual(recipe["shop_welds"][0]["main_part_handle"], "COL_01")
        self.assertEqual(recipe["bolt_groups"][0]["ny"], 4)


if __name__ == "__main__":
    unittest.main()
