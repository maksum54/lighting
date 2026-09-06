using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace LuxoraRevit
{
    /// <summary>
    /// Alur utama: pilih Room/Space → ukur → POST /api/calc → dialog (nama family + param)
    /// → pasang family di posisi grid yang sudah divalidasi terhadap boundary ruang.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CalcCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            if (doc == null) return Result.Cancelled;

            try
            {
                // 1) Pilih Room/Space
                if (!TryPickSpatialElement(uidoc, out SpatialElement room, out string pickNote))
                {
                    TaskDialog.Show("Luxora", pickNote);
                    return Result.Cancelled;
                }

                // 2) Baca geometri ruang (outline + bounding box + area)
                RoomGeometry geo;
                try { geo = RoomGeometry.Read(room); }
                catch (Exception ex)
                {
                    TaskDialog.Show("Luxora", "Gagal membaca geometri ruang:\n" + ex.Message);
                    return Result.Cancelled;
                }

                if (geo.Outline == null || geo.Outline.Count == 0)
                {
                    TaskDialog.Show("Luxora", "Ruang ini tidak memiliki boundary yang terhitung.\n" +
                                              "(Pastikan ruang sudah diberi batas/diplace, lalu coba lagi.)");
                    return Result.Cancelled;
                }

                if (geo.LengthM < 1 || geo.WidthM < 1 || geo.LengthM > 200 || geo.WidthM > 200)
                {
                    TaskDialog.Show("Luxora",
                        $"Ukuran ruang terbaca {geo.LengthM:0.00} × {geo.WidthM:0.00} m — di luar rentang yang didukung " +
                        "kalkulasi (1–200 m per sisi).\nPeriksa satuan model atau boundary ruangnya.");
                    return Result.Cancelled;
                }

                // 3) Dialog input: nilai default dari ruang / web, serta nama family.
                CalcDialog dlg = new CalcDialog(room, geo);
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                if (string.IsNullOrWhiteSpace(dlg.FamilyName))
                {
                    TaskDialog.Show("Luxora", "Nama family lighting fixture wajib diisi.");
                    return Result.Cancelled;
                }

                // 4) Hitung via website
                CalcResult result;
                try
                {
                    using (CalcClient client = new CalcClient(dlg.BaseUrl))
                    {
                        result = client.Calculate(new CalcRequest
                        {
                            L = dlg.LengthM,
                            W = dlg.WidthM,
                            H = dlg.CeilingM,
                            wp = dlg.WorkPlaneM,
                            F = dlg.Lumens,
                            P = dlg.Watts,
                            E = dlg.TargetLux,
                            lumType = dlg.LumType,
                            refC = 0.70,
                            refW = 0.50,
                            llf = 0.812,
                            cuManual = false,
                            cu = null
                        });
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Luxora — gagal terhubung ke website",
                        "Tidak dapat memanggil endpoint kalkulasi:\n" + ex.Message +
                        "\n\nPastikan server Luxora berjalan / URL benar (mis. http://localhost:8787 atau https://<vercel>.vercel.app).");
                    return Result.Failed;
                }

                if (result.positionsM == null || result.positionsM.Count == 0)
                {
                    TaskDialog.Show("Luxora", "Website mengembalikan 0 posisi lampu. Cek parameter.");
                    return Result.Cancelled;
                }

                // 5) Pasang family lighting fixture
                Level level = room.LevelId != null && room.LevelId != ElementId.InvalidElementId
                    ? doc.GetElement(room.LevelId) as Level : null;
                FamilyPlacer.Outcome outcome = FamilyPlacer.Place(doc, room, geo, dlg.FamilyName, level, dlg.OffsetZ, result);

                // 6) Ringkasan
                string status = outcome.Placed > 0
                    ? $"Dipasang {outcome.Placed} lampu."
                    : "Tidak ada lampu yang berhasil dipasang.";

                string notes = "";
                if (outcome.Nudged > 0)
                    notes += $"\n{outcome.Nudged} lampu digeser sedikit agar tetap di dalam boundary ruang.";
                if (outcome.Skipped > 0)
                    notes += $"\n{outcome.Skipped} titik dilewati karena berada di luar boundary ruang (atau di area lubang).";
                if (outcome.Failed > 0)
                    notes += $"\n{outcome.Failed} titik gagal dibuat instance-nya oleh Revit.";
                if (!string.IsNullOrWhiteSpace(outcome.Error))
                    notes += "\n\nCatatan: " + outcome.Error;
                if (outcome.Placed == 0 && outcome.Skipped == result.positionsM.Count)
                    notes += "\n\nSeluruh titik jatuh di luar ruang. Cek apakah Room/Space yang dipilih benar-benar " +
                             "tertutup boundary-nya, atau apakah P×L di dialog jauh lebih besar dari ruang sebenarnya.";

                TaskDialog.Show("Luxora — selesai",
                    $"{status}\nGrid {result.cols} × {result.rows} ({result.n} titik) pada {dlg.LengthM:0.00} × {dlg.WidthM:0.00} m.\n" +
                    $"Eavg ≈ {Math.Round(result.actual)} lx · U₀ {result.u0:0.00} · LPD {result.lpd:0.0} W/m² · {result.lumLabel}.{notes}");
                return outcome.Placed > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Luxora — error", ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Minta user memilih satu Room/Space yang punya boundary.
        /// </summary>
        private bool TryPickSpatialElement(UIDocument uidoc, out SpatialElement room, out string note)
        {
            room = null;
            note = "";
            ISelectionFilter filter = new SpatialElementSelectionFilter();
            try
            {
                Reference picked = uidoc.Selection.PickObject(ObjectType.Element, filter,
                    "Pilih Room atau Space yang akan dihitung lampunya (ESC utk batal).");
                Element el = uidoc.Document.GetElement(picked.ElementId);
                SpatialElement se = el as SpatialElement;
                if (se == null)
                {
                    note = "Objek terpilih bukan Room/Space yang valid.";
                    return false;
                }
                room = se;
                return true;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                note = "Pemilihan dibatalkan.";
                return false;
            }
            catch (Exception ex)
            {
                note = "Gagal memilih: " + ex.Message;
                return false;
            }
        }
    }

    /// <summary>Hanya boleh memilih Room atau Space yang benar-benar terplace (punya luas).</summary>
    public class SpatialElementSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            SpatialElement se = elem as SpatialElement;
            if (se == null) return false;
            if (!(se is Room) && !(se is Space)) return false;
            try { return se.Area > 1e-6; }   // ruang yang belum diplace luasnya 0
            catch { return false; }
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}
