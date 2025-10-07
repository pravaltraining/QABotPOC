using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Text;

namespace QABotPOC
{
    public static class EmbeddingHelper
    {
        public static async Task<float[]> GetEmbeddingAsync(string input)
        {
            using var httpClient = new HttpClient();
            
            httpClient.DefaultRequestHeaders.Add("api-key", AppSettings.ApiKey);

            var requestBody = new
            {
                input = input,
                model = AppSettings.EmbeddingDeployment
            };

            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

            var url = $"{AppSettings.Endpoint}openai/deployments/{AppSettings.EmbeddingDeployment}/embeddings?api-version=2023-05-15";

            var response = await httpClient.PostAsync(url, content);
            var json = await response.Content.ReadAsStringAsync();

            var result = JObject.Parse(json);
            return result["data"][0]["embedding"].ToObject<float[]>();
        }

        public static float CosineSimilarity(float[] a, float[] b)
        {
            float dot = 0, normA = 0, normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }
            return dot / ((float)Math.Sqrt(normA) * (float)Math.Sqrt(normB));
        }
    }

}
