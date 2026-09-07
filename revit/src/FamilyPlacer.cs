using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace LuxoraRevit
{
    /// <summary>
    /// Menempatkan family lighting fixture pada posisi grid hasil website.
    /// Titik divalidasi terhadap boundary Room/Space LEBIH DULU (bukan dipasang lalu dihapus):
    /// titik yang jatuh di luar digeser ke posisi sah terdekat, dan hanya dilewati bila
    /// benar-benar tidak ada ruang untuknya.
    /// </summary>
    public static class FamilyPlacer
    {
        public const double Ft2M = 0.3048;
        public const double M2Ft = 1 / 0.3048;

        public class Outcome
        {
            public int Placed { get; set; }
            /// <summary>Titik yang digeser ke dalam ruang (ruang bentuk-L / dekat lubang).</summary>
            public int Nudged { get; set; }
            /// <summary>Titik yang dilewati karena tidak ada posisi sah di sekitarnya.</summary>
            public int Skipped { get; set; }
            /// <summary>Titik yang gagal dibuat instance-nya oleh Revit.</summary>
            public int Failed { get; set; }
            /// <summary>Lampu lama di dalam ruang yang dihapus sebelum pemasangan (revisi lampu).</summary>
            public int RemovedExisting { get; set; }
            public string Error { get; set; }
            public List<ElementId> PlacedIds { get; } = new List<ElementId>();
        }

        /// <summary>
        /// Cari symbol family lighting fixture dari NAMA FAMILY (bukan nama tipe).
        /// Bila tidak ketemu, nama juga dicocokkan ke nama TIPE dan format "Family: Tipe",
        /// supaya masukan pengguna yang wajar tetap diterima.
        /// </summary>
        public static FamilySymbol FindSymbolByName(Document doc, string familyName)
        {
            string name = (familyName ?? "").Trim();
            if (name.Length == 0) return null;

            // "Family: Tipe" → ambil bagian family-nya juga sebagai kandidat.
            string famPart = name;
            int colon = name.IndexOf(':');
            if (colon > 0) famPart = name.Substring(0, colon).Trim();

            // Category.Id dibandingkan dgn Equals (bukan ==) supaya tidak bergantung pada
            // operator ElementId yang berbeda antar versi API.
            Category lighting = Category.GetCategory(doc, BuiltInCategory.OST_LightingFixtures);
            ElementId lightingId = lighting != null ? lighting.Id : null;

            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => lightingId == null || (fs.Category != null && lightingId.Equals(fs.Category.Id)))
                .ToList();

            // 1) nama family persis
            FamilySymbol hit = symbols.FirstOrDefault(fs => Eq(FamilyNameOf(fs), name))
                            ?? symbols.FirstOrDefault(fs => Eq(FamilyNameOf(fs), famPart));
            if (hit != null) return PreferActive(symbols, hit);

            // 2) "Family: Tipe" persis
            hit = symbols.FirstOrDefault(fs => Eq(FamilyNameOf(fs) + ": " + fs.Name, name));
            if (hit != null) return hit;

            // 3) nama tipe persis
            hit = symbols.FirstOrDefault(fs => Eq(fs.Name, name));
            if (hit != null) return hit;

            // 4) cocok sebagian (pengguna mengetik sepotong nama)
            hit = symbols.FirstOrDefault(fs => Contains(FamilyNameOf(fs), name) || Contains(fs.Name, name));
            return hit;
        }

        private static string FamilyNameOf(FamilySymbol fs) => fs.Family != null ? fs.Family.Name : fs.Name;
        private static bool Eq(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        private static bool Contains(string hay, string needle) =>
            !string.IsNullOrEmpty(hay) && !string.IsNullOrEmpty(needle) &&
            hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Dari family yang sama, dahulukan tipe yang sudah aktif.</summary>
        private static FamilySymbol PreferActive(List<FamilySymbol> all, FamilySymbol hit)
        {
            string fam = FamilyNameOf(hit);
            return all.FirstOrDefault(fs => Eq(FamilyNameOf(fs), fam) && fs.IsActive) ?? hit;
        }

        public static Outcome Place(Document doc, SpatialElement room, RoomGeometry geo, string familyName,
            Level level, double offsetZAboveFloor, CalcResult result, bool replaceExisting)
        {
            var outcome = new Outcome();
            FamilySymbol symbol = FindSymbolByName(doc, familyName);
            if (symbol == null)
            {
                outcome.Error = $"Family lighting fixture “{familyName}” tidak ditemukan di dokumen. " +
                                "Muati family ke dokumen (Insert → Load Family) atau pilih dari daftar di dialog.";
                return outcome;
            }
            if (result == null || result.positionsM == null || result.positionsM.Count == 0)
            {
                outcome.Error = "Tidak ada posisi lampu dari hasil kalkulasi.";
                return outcome;
            }

            // Dimensi yang benar-benar dipakai website saat menyusun grid — dipakai juga saat
            // memetakan balik ke model supaya grid selalu sinkron dgn ruangnya.
            double gridL = result.L > 0 ? result.L : geo.LengthM;
            double gridW = result.W > 0 ? result.W : geo.WidthM;

            // Ketinggian pasang: elevasi level + offset (plafon) — default dari dialog (CeilingM-0.1).
            double levelElev = level != null ? level.Elevation : geo.BaseElevation;
            double z = levelElev + offsetZAboveFloor * M2Ft;   // z dalam kaki

            // Margin tepi: 5 cm, tetapi tidak boleh lebih dari 1/6 jarak antar lampu supaya
            // ruang sempit / grid rapat tidak ikut terbuang.
            double stepFt = Math.Min(gridL / Math.Max(1, result.cols), gridW / Math.Max(1, result.rows)) * M2Ft;
            double marginFt = Math.Min(0.05 * M2Ft, stepFt / 6);
            double snapRadiusFt = stepFt * 0.45;

            // Kandidat host (plafon/atap/lantai) bila family-nya memang butuh host.
            FamilyPlacementType placement = symbol.Family != null
                ? symbol.Family.FamilyPlacementType
                : FamilyPlacementType.OneLevelBased;
            bool needsHost = placement == FamilyPlacementType.OneLevelBasedHosted ||
                             placement == FamilyPlacementType.WorkPlaneBased;
            List<Element> hosts = needsHost ? CollectHostCandidates(doc, geo, z) : new List<Element>();

            using (Transaction tx = new Transaction(doc, "Luxora — pasang lampu"))
            {
                tx.Start();
                if (!symbol.IsActive)
                {
                    try { symbol.Activate(); doc.Regenerate(); }
                    catch { /* jika gagal aktif, tetap dicoba place */ }
                }

                // Revisi lampu: hapus dulu lampu yang sudah ada di dalam ruang, supaya hasil
                // baru MENGGANTIKAN lampu lama (bukan menumpuk jadi dobel).
                if (replaceExisting)
                    outcome.RemovedExisting = DeleteExistingInRoom(doc, geo);

                bool giveUp = false;
                foreach (GridPos g in result.positionsM)
                {
                    // Bila beberapa percobaan pertama semuanya ditolak Revit, hentikan —
                    // mencoba puluhan titik lagi hanya membuat Revit menggantung tanpa hasil.
                    if (giveUp) { outcome.Failed++; continue; }

                    XYZ p = geo.ToModel(g.x, g.y, z, gridL, gridW);
                    XYZ ok = geo.SnapInside(p, snapRadiusFt, marginFt);
                    if (ok == null) { outcome.Skipped++; continue; }
                    if (!ReferenceEquals(ok, p)) outcome.Nudged++;

                    try
                    {
                        FamilyInstance inst = Create(doc, symbol, ok, level, hosts, needsHost);
                        if (inst == null) { outcome.Failed++; continue; }
                        outcome.PlacedIds.Add(inst.Id);
                        outcome.Placed++;
                    }
                    catch (Exception ex)
                    {
                        outcome.Failed++;
                        if (string.IsNullOrWhiteSpace(outcome.Error)) outcome.Error = ex.Message;
                        if (outcome.Placed == 0 && outcome.Failed >= 3) giveUp = true;
                    }
                }

                if (outcome.Placed == 0) tx.RollBack();
                else tx.Commit();
            }

            if (outcome.Failed > 0 && string.IsNullOrWhiteSpace(outcome.Error))
                outcome.Error = "Revit menolak menempatkan family ini pada titik yang diminta.";
            if (outcome.Placed == 0 && outcome.Failed > 0 && needsHost)
                outcome.Error += "\nFamily ini berbasis host/work plane — pastikan ada plafon (ceiling) " +
                                 "pada ketinggian pemasangan, atau pakai family lighting fixture non-hosted.";
            return outcome;
        }

        /// <summary>
        /// Buat satu instance. Untuk family berbasis host dicoba: face plafon → elemen host →
        /// level; untuk family biasa: level → host. Urutan ini membuat sebagian besar family
        /// lighting fixture bawaan Revit (hosted maupun tidak) tetap bisa dipasang.
        /// </summary>
        private static FamilyInstance Create(Document doc, FamilySymbol symbol, XYZ p, Level level,
            List<Element> hosts, bool needsHost)
        {
            Exception first = null;

            if (needsHost)
            {
                FamilyInstance byFace = TryFace(doc, symbol, p, hosts, ref first);
                if (byFace != null) return byFace;
                FamilyInstance byHost = TryHostElement(doc, symbol, p, level, hosts, ref first);
                if (byHost != null) return byHost;
                FamilyInstance byLevel = TryLevel(doc, symbol, p, level, ref first);
                if (byLevel != null) return byLevel;
            }
            else
            {
                FamilyInstance byLevel = TryLevel(doc, symbol, p, level, ref first);
                if (byLevel != null) return byLevel;
                FamilyInstance byHost = TryHostElement(doc, symbol, p, level, hosts, ref first);
                if (byHost != null) return byHost;
            }

            if (first != null) throw first;
            return null;
        }

        private static FamilyInstance TryLevel(Document doc, FamilySymbol symbol, XYZ p, Level level, ref Exception first)
        {
            try
            {
                return level != null
                    ? doc.Create.NewFamilyInstance(p, symbol, level, StructuralType.NonStructural)
                    : doc.Create.NewFamilyInstance(p, symbol, StructuralType.NonStructural);
            }
            catch (Exception ex) { if (first == null) first = ex; return null; }
        }

        private static FamilyInstance TryHostElement(Document doc, FamilySymbol symbol, XYZ p, Level level,
            List<Element> hosts, ref Exception first)
        {
            foreach (Element host in HostsAt(hosts, p))
            {
                try { return doc.Create.NewFamilyInstance(p, symbol, host, level, StructuralType.NonStructural); }
                catch (Exception ex) { if (first == null) first = ex; }
            }
            return null;
        }

        private static FamilyInstance TryFace(Document doc, FamilySymbol symbol, XYZ p, List<Element> hosts, ref Exception first)
        {
            foreach (Element host in HostsAt(hosts, p))
            {
                PlanarFace face = BottomFaceAt(host, p);
                if (face == null || face.Reference == null) continue;
                try
                {
                    XYZ onFace = new XYZ(p.X, p.Y, face.Origin.Z);
                    return doc.Create.NewFamilyInstance(face.Reference, onFace, XYZ.BasisX, symbol);
                }
                catch (Exception ex) { if (first == null) first = ex; }
            }
            return null;
        }

        /// <summary>Plafon/atap/lantai yang bidang XY-nya menaungi ruang & dekat ketinggian pasang.</summary>
        private static List<Element> CollectHostCandidates(Document doc, RoomGeometry geo, double zFeet)
        {
            var cats = new[] { BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Floors };
            var found = new List<KeyValuePair<double, Element>>();
            foreach (BuiltInCategory bic in cats)
            {
                FilteredElementCollector col;
                try
                {
                    col = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType();
                }
                catch { continue; }
                foreach (Element e in col)
                {
                    BoundingBoxXYZ bb = null;
                    try { bb = e.get_BoundingBox(null); } catch { }
                    if (bb == null) continue;
                    double mid = (bb.Min.Z + bb.Max.Z) / 2;
                    double dz = Math.Abs(mid - zFeet);
                    if (dz > 6.0) continue;                 // > ~1.8 m dari ketinggian pasang: abaikan
                    found.Add(new KeyValuePair<double, Element>(dz, e));
                }
            }
            return found.OrderBy(kv => kv.Key).Select(kv => kv.Value).Take(40).ToList();
        }

        private static IEnumerable<Element> HostsAt(List<Element> hosts, XYZ p)
        {
            const double tol = 0.01;
            foreach (Element e in hosts)
            {
                BoundingBoxXYZ bb = null;
                try { bb = e.get_BoundingBox(null); } catch { }
                if (bb == null) continue;
                if (p.X < bb.Min.X - tol || p.X > bb.Max.X + tol) continue;
                if (p.Y < bb.Min.Y - tol || p.Y > bb.Max.Y + tol) continue;
                yield return e;
            }
        }

        /// <summary>Bidang datar menghadap bawah (permukaan bawah plafon) yang memuat titik p.</summary>
        private static PlanarFace BottomFaceAt(Element host, XYZ p)
        {
            Options opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement ge = null;
            try { ge = host.get_Geometry(opt); } catch { }
            if (ge == null) return null;

            PlanarFace best = null;
            foreach (GeometryObject go in ge)
            {
                Solid solid = go as Solid;
                if (solid == null)
                {
                    GeometryInstance gi = go as GeometryInstance;
                    if (gi == null) continue;
                    foreach (GeometryObject g2 in gi.GetInstanceGeometry())
                    {
                        Solid s2 = g2 as Solid;
                        if (s2 != null) best = PickFace(s2, p, best);
                    }
                    continue;
                }
                best = PickFace(solid, p, best);
            }
            return best;
        }

        private static PlanarFace PickFace(Solid solid, XYZ p, PlanarFace best)
        {
            if (solid == null || solid.Faces == null) return best;
            foreach (Face f in solid.Faces)
            {
                PlanarFace pf = f as PlanarFace;
                if (pf == null) continue;
                if (pf.FaceNormal == null || pf.FaceNormal.Z > -0.9) continue;   // hanya muka menghadap bawah
                IntersectionResult ir = null;
                try { ir = pf.Project(new XYZ(p.X, p.Y, pf.Origin.Z)); } catch { }
                if (ir == null) continue;
                if (best == null || pf.Origin.Z < best.Origin.Z) best = pf;       // plafon terendah = paling dekat
            }
            return best;
        }

        /// <summary>Apakah titik (kaki) berada di dalam outline dan tidak di dalam lubang.</summary>
        public static bool PointInRoom(XYZ point, RoomGeometry geo)
        {
            return geo.ContainsPoint(point, 0.05 * M2Ft);
        }

        /// <summary>
        /// Hapus semua lighting fixture (FamilyInstance) yang titik lokasinya berada di dalam
        /// boundary ruang. Margin sedikit negatif = tidak ada jarak-minimum ke tepi, supaya
        /// lampu yang menempel dinding/tak jauh dari tepi ikut terhapus saat revisi.
        /// </summary>
        private static int DeleteExistingInRoom(Document doc, RoomGeometry geo)
        {
            Category cat = Category.GetCategory(doc, BuiltInCategory.OST_LightingFixtures);
            if (cat == null) return 0;

            var toDelete = new List<ElementId>();
            FilteredElementCollector col;
            try
            {
                col = new FilteredElementCollector(doc).OfCategoryId(cat.Id).OfClass(typeof(FamilyInstance));
            }
            catch { return 0; }

            foreach (FamilyInstance fi in col)
            {
                LocationPoint lp = null;
                try { lp = fi.Location as LocationPoint; } catch { }
                if (lp == null) continue;
                if (geo.ContainsPoint(lp.Point, -0.05 * M2Ft)) toDelete.Add(fi.Id);
            }

            int removed = 0;
            foreach (ElementId id in toDelete)
            {
                try { doc.Delete(id); removed++; } catch { }
            }
            return removed;
        }
    }
}
