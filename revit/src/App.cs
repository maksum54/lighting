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

        /// <summary>
        /// Catatan startup di %TEMP%\LuxoraRevit-startup.log. Gunanya untuk membedakan dua
        /// kegagalan yang sama-sama terlihat "senyap" dari sisi Revit:
        ///   - berkas log TIDAK ada  → Revit tidak pernah memuat DLL ini (manifest/keamanan/versi).
        ///   - berkas log ADA        → DLL dimuat; isinya menunjukkan langkah mana yang gagal.
        /// Menulis log tidak boleh pernah melempar exception.
        /// </summary>
        public static string LogPath
        {
            get
            {
                try { return Path.Combine(Path.GetTempPath(), "LuxoraRevit-startup.log"); }
                catch { return null; }
            }
        }

        internal static void Log(string pesan)
        {
            try
            {
                string path = LogPath;
                if (string.IsNullOrEmpty(path)) return;
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + pesan + Environment.NewLine);
            }
            catch { /* logging tidak boleh menggagalkan add-in */ }
        }

        public Result OnStartup(UIControlledApplication application)
        {
            string asmPath = "";
            try { asmPath = Assembly.GetExecutingAssembly().Location; } catch { }
            Log("OnStartup mulai — assembly: " + (string.IsNullOrEmpty(asmPath) ? "(tidak diketahui)" : asmPath));
            try { Log("  Revit " + application.ControlledApplication.VersionNumber + " (" + application.ControlledApplication.VersionBuild + ")"); }
            catch { }

            try
            {
                // Tab boleh sudah ada (mis. add-in dimuat ulang); jangan crash.
                try { application.CreateRibbonTab(TabName); Log("  tab '" + TabName + "' dibuat"); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { Log("  tab '" + TabName + "' sudah ada"); }

                RibbonPanel panel = GetOrCreatePanel(application);
                if (panel == null)
                {
                    Log("  GAGAL: panel '" + PanelName + "' tidak bisa dibuat.");
                    TaskDialog.Show("Luxora — gagal memuat", "Panel Ribbon '" + PanelName + "' tidak bisa dibuat.");
                    return Result.Failed;
                }
                Log("  panel '" + PanelName + "' siap");

                PushButtonData btn = new PushButtonData(
                    "LuxoraCalc",
                    "Hitung & Pasang\nLampu",
                    asmPath,
                    "LuxoraRevit.CalcCommand");

                // Ikon: bungkus sederhana bila file ikon tersedia di samping DLL.
                string iconPath = Path.Combine(Path.GetDirectoryName(asmPath) ?? "", "Luxora.ico");
                if (File.Exists(iconPath))
                {
                    try { btn.LargeImage = new BitmapImage(new Uri(iconPath)); }
                    catch { /* ikon opsional */ }
                }

                btn.ToolTip = "Pilih Room/Space lalu hitung via website Luxora dan pasang lampu sesuai jumlah, " +
                              "hapus otomatis bila melewati batas ruang.";
                btn.LongDescription = "1) Klik tombol, lalu pilih Room atau Space di dokumen aktif.\n" +
                                      "2) Add-in mengukur P×L ruang dan memanggil /api/calc website Luxora.\n" +
                                      "3) Isi nama family lighting fixture yang ada di dokumen, lalu OK.\n" +
                                      "4) Lampu dipasang otomatis; yang berada di luar boundary room/space dihapus.";

                panel.AddItem(btn);
                Log("  tombol ditambahkan — OnStartup selesai (tab '" + TabName + "' siap dipakai)");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log("  GAGAL: " + ex.GetType().Name + " — " + ex.Message);
                Log(ex.StackTrace ?? "");
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
            Log("OnShutdown");
            return Result.Succeeded;
        }
    }
}
