using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace LuxoraRevit
{
    /// <summary>
    /// Menempatkan family lighting fixture pada posisi grid hasil website, lalu
    /// menghapus otomatis lampu yang berada di luar boundary Room/Space (termasuk lubang).
    /// </summary>
    public static class FamilyPlacer
    {
        public const double Ft2M = 0.3048;

        public class Outcome
        {
            public int Placed { get; set; }
            public int Removed { get; set; }
            public string Error { get; set; }
        }

        /// <summary>
        /// Cari symbol family lighting fixture dari NAMA FAMILY (bukan nama tipe).
        /// Mengembalikan symbol aktif pertama dari family yang cocok.
        /// </summary>
        public static FamilySymbol FindSymbolByName(Document doc, string familyName)
        {
            string name = (familyName ?? "").Trim();
            if (name.Length == 0) return null;

            Category lighting = Category.GetCategory(doc, BuiltInCategory.OST_LightingFixtures);
            ElementId lightingId = lighting != null ? lighting.Id : null;

            FilteredElementCollector col = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
            foreach (FamilySymbol fs in col)
            {
                if (lightingId != null && fs.Category != null && fs.Category.Id != lightingId) continue;
                string fam = fs.Family != null ? fs.Family.Name : fs.Name;
                if (string.Equals(fam, name, StringComparison.OrdinalIgnoreCase)) return fs;
            }
            return null;
        }

        public static Outcome Place(Document doc, SpatialElement room, RoomGeometry geo, string familyName,
            Level level, double offsetZAboveFloor, CalcResult result)
        {
            var outcome = new Outcome();
            FamilySymbol symbol = FindSymbolByName(doc, familyName);
            if (symbol == null)
            {
                outcome.Error = $"Family lighting fixture “{familyName}” tidak ditemukan di dokumen. " +
                                "Muati family ke dokumen atau periksa ejaan nama family.";
                return outcome;
            }
            if (result == null || result.positionsM == null || result.positionsM.Count == 0)
            {
                outcome.Error = "Tidak ada posisi lampu dari hasil kalkulasi.";
                return outcome;
            }

            // Ketinggian pasang: elevasi level + offset (plafon) — default dari dialog (CeilingM-0.1).
            double levelElev = level != null ? level.Elevation : geo.BaseElevation;
            double z = levelElev + offsetZAboveFloor / Ft2M;   // z dalam kaki

            var placed = new List<FamilyInstance>();
            var toDelete = new List<FamilyInstance>();

            using (Transaction tx = new Transaction(doc, "Luxora — pasang lampu"))
            {
                tx.Start();
                if (!symbol.IsActive)
                {
                    try { symbol.Activate(); }
                    catch { /* jika gagal aktif, lanjut coba place */ }
                }

                foreach (GridPos g in result.positionsM)
                {
                    XYZ p = geo.ToModel(g.x, g.y, z);
                    FamilyInstance inst = doc.Create.NewFamilyInstance(p, symbol, null, level,
                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    placed.Add(inst);
                }

                // filter: hapus yg di luar outline atau di dalam lubang. Margin biar tepi aman.
                foreach (FamilyInstance inst in placed)
                {
                    LocationPoint lp = inst.Location as LocationPoint;
                    if (lp == null) { toDelete.Add(inst); continue; }
                    XYZ pt = lp.Point;
                    if (!PointInRoom(pt, geo)) toDelete.Add(inst);
                }

                if (toDelete.Count > 0)
                {
                    foreach (FamilyInstance inst in toDelete) { try { doc.Delete(inst.Id); } catch { } }
                }
                outcome.Placed = placed.Count - toDelete.Count;
                outcome.Removed = toDelete.Count;
                tx.Commit();
            }
            return outcome;
        }

        /// <summary>Apakah titik (kaki) berada di dalam outline dan tidak di dalam lubang.</summary>
        public static bool PointInRoom(XYZ point, RoomGeometry geo)
        {
            if (!Geo2D.PointInPolygon(point, geo.Outline)) return false;
            if (geo.Holes != null)
            {
                foreach (List<XYZ> hole in geo.Holes)
                {
                    if (Geo2D.PointInPolygon(point, hole)) return false;  // di dalam lubang = di luar ruang
                }
            }
            // margin kecil dari tepi supaya lampu tidak "nempel" dinding
            double minEdge = 0.05 / Ft2M; // 5 cm
            if (Geo2D.DistToPolygon(point, geo.Outline) < minEdge) return false;
            return true;
        }
    }
}
