using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace LuxoraRevit
{
    /// <summary>
    /// IExternalApplication — mendaftarkan tombol "Luxora Lighting" di Ribbon Revit.
    /// </summary>
    public class App : IExternalApplication
    {
        public const string TabName = "Luxora";
        public const string PanelName = "Integrasi";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // Tab boleh sudah ada (mis. add-in dimuat ulang); jangan crash.
                try { application.CreateRibbonTab(TabName); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { /* tab sudah ada */ }

                RibbonPanel panel = GetOrCreatePanel(application);
                if (panel == null) return Result.Failed;

                string asmPath = Assembly.GetExecutingAssembly().Location;

                PushButtonData btn = new PushButtonData(
                    "LuxoraCalc",
                    "Hitung & Pasang\nLampu",
                    asmPath,
                    "LuxoraRevit.CalcCommand");

                // Ikon: PNG di samping DLL. (BitmapImage WPF tidak bisa memuat .ico,
                // jadi pakai PNG 32px untuk LargeImage dan 16px untuk Image.)
                string dir = Path.GetDirectoryName(asmPath) ?? "";
                string largeIcon = Path.Combine(dir, "Luxora32.png");
                string smallIcon = Path.Combine(dir, "Luxora16.png");
                try
                {
                    if (File.Exists(largeIcon))
                        btn.LargeImage = new BitmapImage(new Uri(largeIcon));
                    if (File.Exists(smallIcon))
                        btn.Image = new BitmapImage(new Uri(smallIcon));
                }
                catch { /* ikon opsional */ }

                btn.ToolTip = "Pilih Room/Space lalu hitung via website Luxora dan pasang lampu sesuai jumlah, " +
                              "hapus otomatis bila melewati batas ruang.";
                btn.LongDescription = "1) Klik tombol, lalu pilih Room atau Space di dokumen aktif.\n" +
                                      "2) Add-in mengukur P×L ruang dan memanggil /api/calc website Luxora.\n" +
                                      "3) Isi nama family lighting fixture yang ada di dokumen, lalu OK.\n" +
                                      "4) Lampu dipasang otomatis; yang berada di luar boundary room/space dihapus.";

                panel.AddItem(btn);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Luxora — gagal memuat", "Terjadi kesalahan saat memuat add-in:\n" + ex.Message);
                return Result.Failed;
            }
        }

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication application)
        {
            // Cari panel di tab yang sudah ada lebih dulu.
            foreach (RibbonPanel p in application.GetRibbonPanels(TabName))
            {
                if (string.Equals(p.Name, PanelName, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return application.CreateRibbonPanel(TabName, PanelName);
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
