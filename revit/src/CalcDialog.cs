using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;

namespace LuxoraRevit
{
    /// <summary>
    /// Dialog input parameter + nama family lighting fixture (harus cocok dgn family di dokumen).
    /// Menampilkan P×L hasil ukur otomatis & preview jumlah lampu dari website.
    /// </summary>
    public class CalcDialog : Form
    {
        private readonly SpatialElement _room;
        private readonly RoomGeometry _geo;
        private readonly Document _doc;
        private readonly List<string> _familyNames = new List<string>();

        private readonly TextBox _length = new TextBox();
        private readonly TextBox _width = new TextBox();
        private readonly TextBox _ceil = new TextBox();
        private readonly TextBox _wp = new TextBox();
        private readonly TextBox _lux = new TextBox();
        private readonly TextBox _lumen = new TextBox();
        private readonly TextBox _watt = new TextBox();
        private readonly ComboBox _lumType = new ComboBox();
        private readonly ComboBox _family = new ComboBox();
        private readonly TextBox _baseUrl = new TextBox();
        private readonly Label _preview = new Label();
        private System.Windows.Forms.Timer _previewTimer;

        public CalcDialog(SpatialElement room, RoomGeometry geo) : this(room, geo, room.Document)
        {
        }

        public CalcDialog(SpatialElement room, RoomGeometry geo, Document doc)
        {
            _room = room;
            _geo = geo;
            _doc = doc;
            LengthM = geo.LengthM;
            WidthM = geo.WidthM;
            Build();
            LoadFamilies();
        }

        // -- hasil yang dibaca pemanggil --
        public string FamilyName => _family.Text.Trim();
        public string BaseUrl => _baseUrl.Text.Trim();
        public string LevelName { get; private set; }
        public double LengthM { get; set; }
        public double WidthM { get; private set; }
        public double CeilingM => Parse(_ceil.Text, 2.7, 1.5, 20);
        public double WorkPlaneM => Parse(_wp.Text, 0.75, 0.2, 5);
        public double TargetLux => Parse(_lux.Text, 300, 10, 100000);
        public double Lumens => Parse(_lumen.Text, 3000, 100, 100000);
        public double Watts => Parse(_watt.Text, 36, 1, 2000);
        public string LumType => _lumType.SelectedValue as string ?? "0.75";
        public double OffsetZ => Math.Max(0.2, CeilingM - 0.1);

        private static double Parse(string s, double dflt, double lo, double hi)
        {
            if (!double.TryParse(s, out double v) || v < lo || v > hi) return dflt;
            return v;
        }

        private void Build()
        {
            Text = "Luxora — Hitung & Pasang Lampu";
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            Width = 560;
            Height = 640;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // kolom kiri: informasi + parameter
            var left = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            string roomName = "";
            if (_room is Room r) roomName = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
            else if (_room is Space sp) roomName = sp.Name ?? "";
            Level level = _room.LevelId != null && _room.LevelId != ElementId.InvalidElementId ? _doc.GetElement(_room.LevelId) as Level : null;
            LevelName = level?.Name ?? "";

            AddInfo(left, "Ruangan", string.IsNullOrWhiteSpace(roomName) ? "(tanpa nama)" : roomName, true);
            AddInfo(left, "Tipe ruang", _room is Room ? "Room" : "Space", true);
            AddInfo(left, "Luas", _geo.AreaM2.ToString("0.0") + " m²", true);

            AddLabel(left, "Panjang terukur (m)");
            _length.Text = _geo.LengthM.ToString("0.00");
            left.Controls.Add(_length, 1, left.RowCount - 1);
            AddLabel(left, "Lebar terukur (m)");
            _width.Text = _geo.WidthM.ToString("0.00");
            left.Controls.Add(_width, 1, left.RowCount - 1);
            AddLabel(left, "Tinggi plafon (m)");
            _ceil.Text = "2.7";
            left.Controls.Add(_ceil, 1, left.RowCount - 1);
            AddLabel(left, "Bidang kerja (m)");
            _wp.Text = "0.75";
            left.Controls.Add(_wp, 1, left.RowCount - 1);

            AddLabel(left, "Target (lux)");
            _lux.Text = "300";
            left.Controls.Add(_lux, 1, left.RowCount - 1);
            AddLabel(left, "Lumen / lampu");
            _lumen.Text = "3000";
            left.Controls.Add(_lumen, 1, left.RowCount - 1);
            AddLabel(left, "Watt / lampu");
            _watt.Text = "36";
            left.Controls.Add(_watt, 1, left.RowCount - 1);

            AddLabel(left, "Jenis luminaire");
            _lumType.DropDownStyle = ComboBoxStyle.DropDownList;
            _lumType.DataSource = LumOptions();
            _lumType.DisplayMember = "Text";
            _lumType.ValueMember = "Id";
            _lumType.SelectedValue = "0.75";
            left.Controls.Add(_lumType, 1, left.RowCount - 1);

            AddLabel(left, "Family lighting fixture");
            _family.DropDownStyle = ComboBoxStyle.DropDown;
            _family.Width = 360;
            _family.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            left.Controls.Add(_family, 1, left.RowCount - 1);
            AddRowHint(left, "Pilih dari daftar family Lighting Fixtures di dokumen, atau ketik nama family persis (contoh: “LED Panel 600x600”).");

            AddLabel(left, "Base URL website Luxora");
            _baseUrl.Text = "http://localhost:8787";
            left.Controls.Add(_baseUrl, 1, left.RowCount - 1);
            AddRowHint(left, "Server lokal (node server.js) atau Vercel. Add-in memanggil POST /api/calc.");

            _preview.AutoSize = true;
            _preview.ForeColor = Color.DimGray;
            _preview.Text = "Preview: —";
            left.RowCount++;
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _preview.Margin = new Padding(0, 8, 0, 0);
            left.Controls.Add(_preview, 0, left.RowCount - 1);
            left.SetColumnSpan(_preview, 2);
            root.Controls.Add(left, 0, 0);

            // tombol — validasi nama family saat OK
            Button ok = new Button { Text = "Hitung & Pasang", DialogResult = DialogResult.None, Width = 140 };
            Button cancel = new Button { Text = "Batal", DialogResult = DialogResult.Cancel, Width = 90 };
            ok.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(FamilyName))
                {
                    MessageBox.Show("Nama family lighting fixture wajib diisi.", "Luxora", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                DialogResult = DialogResult.OK;
            };
            TableLayoutPanel btns = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btns.Controls.Add(ok, 0, 0);
            btns.Controls.Add(cancel, 1, 0);
            Controls.Add(btns);
            Controls.Add(root);
            AcceptButton = ok;
            CancelButton = cancel;

            // preview live (debounce) via website
            _previewTimer = new System.Windows.Forms.Timer { Interval = 700 };
            _previewTimer.Tick += async (s, e) =>
            {
                _previewTimer.Stop();
                await UpdatePreviewAsync();
            };
            foreach (Control c in new Control[] { _length, _width, _ceil, _wp, _lux, _lumen, _watt, _lumType, _baseUrl })
            {
                if (c is TextBox tb) tb.TextChanged += (s, e) => _previewTimer.Start();
            }
            _lumType.SelectedIndexChanged += (s, e) => _previewTimer.Start();
            _baseUrl.TextChanged += (s, e) => _previewTimer.Start();
        }

        private async Task UpdatePreviewAsync()
        {
            _preview.Text = "Preview: menghitung…";
            _preview.ForeColor = Color.DimGray;
            try
            {
                CalcResult r = await Task.Run(() =>
                {
                    using CalcClient client = new CalcClient(BaseUrl);
                    return client.Calculate(new CalcRequest
                    {
                        L = Parse(_length.Text, LengthM, 1, 200),
                        W = Parse(_width.Text, WidthM, 1, 200),
                        H = CeilingM, wp = WorkPlaneM,
                        F = Lumens, P = Watts, E = TargetLux,
                        lumType = LumType
                    });
                });
                _preview.Text = $"Preview: {r.n} lampu (grid {r.cols}×{r.rows}) · Eav ≈ {r.actual:0} lx · U₀ {r.u0:0.00} · LPD {r.lpd:0.0} W/m²";
                _preview.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                _preview.Text = "Preview: gagal — " + ex.Message;
                _preview.ForeColor = Color.Firebrick;
            }
        }

        private void LoadFamilies()
        {
            // Kumpulkan nama family kategori Lighting Fixtures (atau turunannya).
            var ids = new HashSet<string>();
            FilteredElementCollector col = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol));
            Category cat = Category.GetCategory(_doc, BuiltInCategory.OST_LightingFixtures);
            ElementId catId = cat != null ? cat.Id : null;
            foreach (FamilySymbol fs in col)
            {
                if (catId != null && fs.Category != null && fs.Category.Id == catId)
                {
                    string fname = fs.Family != null ? fs.Family.Name : fs.Name;
                    if (!string.IsNullOrWhiteSpace(fname)) ids.Add(fname);
                }
            }
            _familyNames.Clear();
            _familyNames.AddRange(ids.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            _family.Items.Clear();
            foreach (string n in _familyNames) _family.Items.Add(n);
            if (_familyNames.Count > 0)
            {
                _family.SelectedIndex = 0;
                _family.DroppedDown = true; // bantu pengguna lihat opsi
            }
            _family.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _family.AutoCompleteSource = AutoCompleteSource.ListItems;
        }

        private static void AddLabel(TableLayoutPanel t, string text)
        {
            t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 4, 0) };
            t.Controls.Add(l, 0, t.RowCount - 1);
        }

        private void AddRowHint(TableLayoutPanel t, string text)
        {
            t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = new Label { Text = text, AutoSize = true, ForeColor = Color.Gray, MaximumSize = new Size(390, 0), Margin = new Padding(2, 3, 0, 0) };
            t.Controls.Add(l, 0, t.RowCount - 1);
            t.SetColumnSpan(l, 2);
        }

        private void AddInfo(TableLayoutPanel t, string label, string value, bool important)
        {
            AddLabel(t, label);
            var l = new Label { Text = value, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = important ? Color.FromArgb(0, 90, 158) : Color.DimGray, Margin = new Padding(0, 8, 0, 0) };
            t.Controls.Add(l, 1, t.RowCount - 1);
        }

        private class LumOption
        {
            public string Id { get; set; }
            public string Text { get; set; }
        }

        private static LumOption[] LumOptions()
        {
            // Id harus sama persis dgn kunci CU di website /api/calc (calc-core.js).
            return new[]
            {
                new LumOption { Id = "0.75", Text = "Panel LED troffer (direct)" },
                new LumOption { Id = "0.85", Text = "Downlight LED" },
                new LumOption { Id = "0.62", Text = "Lampu gantung / pendant" },
                new LumOption { Id = "bat",  Text = "Linear batten / tube LED" },
                new LumOption { Id = "lb",   Text = "Low bay LED" },
                new LumOption { Id = "hb",   Text = "High bay LED" },
                new LumOption { Id = "flood",Text = "Floodlight LED (outdoor)" },
                new LumOption { Id = "spot", Text = "Spotlight / track LED" }
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _previewTimer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
