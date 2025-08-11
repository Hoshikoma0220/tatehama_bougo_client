using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace tatehama_bougo_client.API
{
    /// <summary>
    /// アクティブな防護無線発報情報
    /// </summary>
    public class ActiveBougoFire
    {
        public string TrainNumber { get; set; } = string.Empty;
        public string Zone { get; set; } = string.Empty;
        public DateTime FireTime { get; set; }
    }

    /// <summary>
    /// 防護無線サーバーAPIクライアント
    /// </summary>
    public static class BougoApiClient
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly string baseUrl = "http://192.168.1.1:5233/api/bougo";

        /// <summary>
        /// 現在アクティブな発報を取得
        /// </summary>
        /// <returns>アクティブな発報リスト</returns>
        public static async Task<List<ActiveBougoFire>> GetActiveFiresAsync()
        {
            try
            {
                var response = await httpClient.GetAsync($"{baseUrl}/active-fires");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var fires = JsonSerializer.Deserialize<List<ActiveBougoFire>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return fires ?? new List<ActiveBougoFire>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ アクティブ発報取得エラー: {ex.Message}");
            }
            return new List<ActiveBougoFire>();
        }

        /// <summary>
        /// 指定ゾーンがアクティブかチェック
        /// </summary>
        /// <param name="zone">チェックするゾーン</param>
        /// <returns>アクティブな場合はtrue</returns>
        public static async Task<bool> CheckZoneActiveAsync(string zone)
        {
            try
            {
                var response = await httpClient.GetAsync($"{baseUrl}/check-zone/{zone}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<bool>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ゾーンアクティブ状態チェックエラー: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// 指定ゾーンに影響するアクティブな発報を取得
        /// </summary>
        /// <param name="zone">チェックするゾーン</param>
        /// <returns>影響する発報リスト</returns>
        public static async Task<List<ActiveBougoFire>> GetAffectingFiresAsync(string zone)
        {
            try
            {
                var response = await httpClient.GetAsync($"{baseUrl}/affecting-fires/{zone}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var fires = JsonSerializer.Deserialize<List<ActiveBougoFire>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return fires ?? new List<ActiveBougoFire>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 影響発報取得エラー: {ex.Message}");
            }
            return new List<ActiveBougoFire>();
        }
    }
}
