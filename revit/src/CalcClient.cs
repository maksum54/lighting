using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LuxoraRevit
{
    /// <summary>Request ke endpoint /api/calc website Luxora.</summary>
    public class CalcRequest
    {
        public double L { get; set; }        // panjang (m)
        public double W { get; set; }        // lebar (m)
        public double H { get; set; }        // tinggi plafon (m)
        public double wp { get; set; }       // tinggi bidang kerja (m)
        public double F { get; set; }        // lumen/lampu
        public double P { get; set; }        // watt/lampu
        public double E { get; set; }        // target lux
        public string lumType { get; set; }  // id luminaire (kunci CU), contoh "0.75"
        public double refC { get; set; } = 0.70;
        public double refW { get; set; } = 0.50;
        public double llf { get; set; } = 0.812;
        public bool cuManual { get; set; } = false;
        public double? cu { get; set; }
    }

    public class GridPos
    {
        public double x { get; set; }   // meter, 0..L
        public double y { get; set; }   // meter, 0..W
    }

    /// <summary>Respons /api/calc — subset field yang dipakai add-in.</summary>
    public class CalcResult
    {
        public double L { get; set; }        // panjang (m) yang benar-benar dipakai server
        public double W { get; set; }        // lebar (m) yang benar-benar dipakai server
        public int n { get; set; }
        public int cols { get; set; }
        public int rows { get; set; }
        public double actual { get; set; }   // lux rata-rata
        public double u0 { get; set; }
        public double lpd { get; set; }
        public string lumLabel { get; set; }
        public List<GridPos> positionsM { get; set; } = new List<GridPos>();
    }

    /// <summary>HTTP client tipis untuk memanggil /api/calc.</summary>
    public class CalcClient : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        private readonly HttpClient _http;

        public CalcClient(string baseUrl)
        {
            string host = (baseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Base URL kosong.");
            if (!host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                host = "http://" + host;

            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.Add("User-Agent", "LuxoraRevitAddin/1.0");
            _http.DefaultRequestHeaders.Add("Accept", "application/json");
            _http.BaseAddress = new Uri(host + "/");
        }

        public CalcResult Calculate(CalcRequest request)
        {
            try
            {
                string json = JsonSerializer.Serialize(request);
                using HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Post, "api/calc")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                using HttpResponseMessage resp = _http.Send(msg);
                string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP {(int)resp.StatusCode} dari /api/calc — {Truncate(ErrorOf(body), 300)}");
                }
                CalcResult result;
                try { result = JsonSerializer.Deserialize<CalcResult>(body, JsonOpts); }
                catch (JsonException jex)
                {
                    throw new Exception("Respons /api/calc bukan JSON yang dikenal: " + jex.Message +
                                        " — pastikan Base URL menunjuk ke server Luxora, bukan halaman lain.");
                }
                if (result == null) throw new Exception("Respons /api/calc tidak valid (JSON kosong).");
                if (result.positionsM == null) result.positionsM = new List<GridPos>();
                return result;
            }
            catch (HttpRequestException hex)
            {
                throw new Exception("Gagal terhubung: " + hex.Message);
            }
            catch (TaskCanceledException)
            {
                throw new Exception("Server tidak merespons dalam 15 detik (timeout).");
            }
        }

        /// <summary>Ambil pesan dari body { "error": "..." } bila ada, supaya dialog lebih jelas.</summary>
        private static string ErrorOf(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            try
            {
                using JsonDocument d = JsonDocument.Parse(body);
                if (d.RootElement.ValueKind == JsonValueKind.Object &&
                    d.RootElement.TryGetProperty("error", out JsonElement e) && e.ValueKind == JsonValueKind.String)
                    return e.GetString();
            }
            catch { /* bukan JSON — kembalikan apa adanya */ }
            return body;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + "…";
        }

        public void Dispose()
        {
            _http?.Dispose();
        }
    }
}
