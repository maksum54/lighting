using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace LuxoraRevit
{
    /// <summary>
    /// Geometri 2D murni (x/y), tanpa ketergantungan dokumen — mudah diuji.
    /// Semua koordinat dalam satuan model Revit (kaki).
    /// </summary>
    public static class Geo2D
    {
        public static double PtX(XYZ p) => p.X;
        public static double PtY(XYZ p) => p.Y;

        /// <summary>Ray-casting: apakah titik berada di dalam polygon (loop tertutup).</summary>
        public static bool PointInPolygon(XYZ point, IList<XYZ> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = PtX(poly[i]), yi = PtY(poly[i]);
                double xj = PtX(poly[j]), yj = PtY(poly[j]);
                bool intersect = ((yi > point.Y) != (yj > point.Y)) &&
                                 (point.X < (xj - xi) * (point.Y - yi) / (yj - yi) + xi);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        public static double DistPointSegment(XYZ p, XYZ a, XYZ b)
        {
            double dx = PtX(b) - PtX(a), dy = PtY(b) - PtY(a);
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return Math.Sqrt(Math.Pow(PtX(p) - PtX(a), 2) + Math.Pow(PtY(p) - PtY(a), 2));
            double t = Math.Max(0, Math.Min(1, ((PtX(p) - PtX(a)) * dx + (PtY(p) - PtY(a)) * dy) / len2));
            double cx = PtX(a) + t * dx, cy = PtY(a) + t * dy;
            return Math.Sqrt(Math.Pow(PtX(p) - cx, 2) + Math.Pow(PtY(p) - cy, 2));
        }

        /// <summary>Jarak minimum titik ke tepi polygon.</summary>
        public static double DistToPolygon(XYZ point, IList<XYZ> poly)
        {
            if (poly == null || poly.Count < 2) return double.PositiveInfinity;
            double best = double.PositiveInfinity;
            for (int i = 0; i < poly.Count - 1; i++)
                best = Math.Min(best, DistPointSegment(point, poly[i], poly[i + 1]));
            best = Math.Min(best, DistPointSegment(point, poly[poly.Count - 1], poly[0]));
            return best;
        }

        /// <summary>Luas polygon (shoelace, kaki²).</summary>
        public static double SignedArea2(IList<XYZ> poly)
        {
            double a = 0;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                XYZ p = poly[i], q = poly[(i + 1) % n];
                a += PtX(p) * PtY(q) - PtX(q) * PtY(p);
            }
            return a / 2;
        }

        public static bool Approximately(double a, double b, double eps = 1e-6) => Math.Abs(a - b) < eps;

        /// <summary>
        /// Buang titik berurutan yang sama/duplikat pada loop. Mengembalikan polygon
        /// tanpa titik penutup ganda.
        /// </summary>
        public static List<XYZ> DedupeLoop(IEnumerable<XYZ> pts)
        {
            var list = new List<XYZ>();
            foreach (XYZ p in pts)
            {
                if (list.Count == 0 ||
                    !(Approximately(PtX(p), PtX(list[list.Count - 1])) && Approximately(PtY(p), PtY(list[list.Count - 1]))))
                    list.Add(new XYZ(PtX(p), PtY(p), 0));
            }
            // bila ujung = awal (loop tertutup), buang penutup
            if (list.Count > 1 &&
                Approximately(PtX(list[0]), PtX(list[list.Count - 1])) &&
                Approximately(PtY(list[0]), PtY(list[list.Count - 1])))
            {
                list.RemoveAt(list.Count - 1);
            }
            return list;
        }
    }

    /// <summary>
    /// Hasil pembacaan geometri sebuah Room/Space:
    /// outline (loop terluar) + lubang + kotak pembatas terorientasi (P×L efektif) dalam meter.
    /// </summary>
    public class RoomGeometry
    {
        public List<XYZ> Outline { get; private set; }          // kaki, XY
        public List<List<XYZ>> Holes { get; private set; }      // kaki, XY (loop dalam)
        public XYZ Center { get; private set; }                 // pusat kotak pembatas (kaki, model)
        public XYZ AxisX { get; private set; }                  // sumbu memanjang (unit)
        public XYZ AxisY { get; private set; }                  // sumbu tegak lurus (unit)
        public double LengthM { get; private set; }             // ukuran memanjang
        public double WidthM { get; private set; }              // ukuran melebar
        public double AreaM2 { get; private set; }
        public double BaseElevation { get; private set; }       // elevasi lantai ruang (kaki)

        private const double Ft2M = 0.3048;

        public static RoomGeometry Read(SpatialElement room)
        {
            Document doc = room.Document;
            SpatialElementBoundaryOptions opts = new SpatialElementBoundaryOptions();
            IList<IList<BoundarySegment>> loops = room.GetBoundarySegments(opts);
            if (loops == null || loops.Count == 0)
                throw new InvalidOperationException("Boundary kosong — ruang belum dibatasi/diplace.");

            List<List<XYZ>> polygons = new List<List<XYZ>>();
            foreach (IList<BoundarySegment> loop in loops)
            {
                List<XYZ> raw = new List<XYZ>();
                foreach (BoundarySegment seg in loop)
                {
                    Curve c = seg.GetCurve();
                    if (c == null) continue;
                    if (c is Line)
                    {
                        raw.Add(c.GetEndPoint(0));
                        raw.Add(c.GetEndPoint(1));
                    }
                    else
                    {
                        // kurva lengkung: ambil sampel
                        foreach (XYZ p in c.Tessellate()) raw.Add(p);
                    }
                }
                if (raw.Count >= 3) polygons.Add(Geo2D.DedupeLoop(raw));
            }
            if (polygons.Count == 0)
                throw new InvalidOperationException("Tidak ada segmen boundary yang terbaca.");

            // Loop luar = yang luas mutlaknya terbesar; sisanya = lubang.
            polygons = polygons.OrderByDescending(p => Math.Abs(Geo2D.SignedArea2(p))).ToList();
            List<XYZ> outline = polygons[0];
            List<List<XYZ>> holes = polygons.Skip(1).ToList();
            // Pastikan orientasi luar = berlawanan arah jarum jam (area positif) utk konsistensi.
            if (Geo2D.SignedArea2(outline) < 0) outline.Reverse();

            // PCA sederhana (covarian 2x2) utk sumbu memanjang.
            double mx = outline.Average(p => p.X), my = outline.Average(p => p.Y);
            double exx = 0, exy = 0, eyy = 0;
            foreach (XYZ p in outline)
            {
                double dx = p.X - mx, dy = p.Y - my;
                exx += dx * dx; exy += dx * dy; eyy += dy * dy;
            }
            double tr = exx + eyy;
            double disc = Math.Sqrt(Math.Max(0, (exx - eyy) * (exx - eyy) + 4 * exy * exy));
            double l1 = (tr + disc) / 2, l2 = (tr - disc) / 2;
            XYZ e1, e2;
            if (disc < 1e-9)
            {
                e1 = new XYZ(1, 0, 0); e2 = new XYZ(0, 1, 0);
            }
            else
            {
                double vx = exy, vy = l1 - exx;             // vektor eigen utk λ1
                double nrm = Math.Sqrt(vx * vx + vy * vy);
                if (nrm < 1e-12) { vx = 1; vy = 0; nrm = 1; }
                e1 = new XYZ(vx / nrm, vy / nrm, 0);
                e2 = new XYZ(-e1.Y, e1.X, 0);
            }
            if (l1 < l2) { XYZ t = e1; e1 = e2; e2 = t; }

            // Rentang proyeksi outline pd kedua sumbu → L×W & pusat kotak.
            double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
            foreach (XYZ p in outline)
            {
                double u = p.X * e1.X + p.Y * e1.Y;
                double v = p.X * e2.X + p.Y * e2.Y;
                minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
            }
            double cu = (minU + maxU) / 2, cv = (minV + maxV) / 2;
            XYZ origin = new XYZ(mx, my, 0);
            XYZ center = origin + e1 * cu + e2 * cv;

            double areaM2 = Math.Abs(Geo2D.SignedArea2(outline)) * Ft2M * Ft2M;

            Level level = room.LevelId != null && room.LevelId != ElementId.InvalidElementId
                ? doc.GetElement(room.LevelId) as Level : null;
            double elev = level != null ? level.Elevation : 0;

            return new RoomGeometry
            {
                Outline = outline,
                Holes = holes,
                Center = center,
                AxisX = e1,
                AxisY = e2,
                LengthM = Math.Max(1, (maxU - minU) * Ft2M),
                WidthM = Math.Max(1, (maxV - minV) * Ft2M),
                AreaM2 = areaM2,
                BaseElevation = elev
            };
        }

        /// <summary>
        /// Peta posisi grid (meter, x memanjang AxisX 0..LengthM, y melebar 0..WidthM)
        /// ke koordinat model (kaki). z diberi elevation absolut.
        /// </summary>
        public XYZ ToModel(double xM, double yM, double zFeet)
        {
            double M2F = 1 / 0.3048;
            XYZ p = Center
                + AxisX * ((xM - LengthM / 2) * M2F)
                + AxisY * ((yM - WidthM / 2) * M2F);
            return new XYZ(p.X, p.Y, zFeet);
        }
    }
}
