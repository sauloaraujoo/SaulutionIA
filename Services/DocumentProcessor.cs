using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using PdfiumViewer;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Drawing;
using System.Buffers.Text;
using SharpToken;

namespace SaulutionIA.Services
{
    public class DocumentProcessor
    {
        private readonly string _openAiKey;
        private readonly string _deepSeekKey;

        public DocumentProcessor(IConfiguration configuration)
        {
            _openAiKey = configuration["OpenAI:ApiKey"] ?? throw new ArgumentNullException("Chave OpenAI não configurada.");
            _deepSeekKey = configuration["DeepSeek:ApiKey"] ?? throw new ArgumentNullException("Chave DeepSeek não configurada.");
        }

        public async Task<string> ExtrairTextoOuImagemBase64(IFormFile file)
        {
            var extensao = Path.GetExtension(file.FileName).ToLower();

            if (extensao == ".txt")
            {
                using var reader = new StreamReader(file.OpenReadStream());
                return await reader.ReadToEndAsync();
            }
            else if (extensao == ".pdf")
            {
                using var pdfStream = file.OpenReadStream();
                using var loaded = PdfiumViewer.PdfDocument.Load(pdfStream);
                var pdfDocument = loaded as IPdfDocument;

                // Renderiza para Bitmap
                using var img = pdfDocument.Render(0, 800, 1000, true);
                using var bmp = new Bitmap(img);

                // Converte Bitmap para ImageSharp Image
                using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(BitmapToBytes(bmp));

                using var ms = new MemoryStream();
                await image.SaveAsPngAsync(ms);

                var base64 = Convert.ToBase64String(ms.ToArray());
                return $"data:image/png;base64,{base64}";
            }
            else if (extensao == ".png" || extensao == ".jpg" || extensao == ".jpeg")
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var base64 = Convert.ToBase64String(ms.ToArray());
                return $"data:image/{extensao.Replace(".", "")};base64,{base64}";
            }

            throw new NotSupportedException("Formato de arquivo não suportado.");
        }

        public async Task<string> IdentificarTipoDocumento(string base64)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiKey);

            // Verifica se é uma imagem válida base64
            bool isImage = base64.StartsWith("data:image");

            if (!isImage)
                return "Apenas imagens base64 são suportadas por enquanto.";

            // Monta o payload para a API do ChatGPT com Vision
            var payload = new
            {
                model = "gpt-4-turbo",
                messages = new[]
                {
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = "Que tipo de documento é esse? (ex: CNH, RG, título de eleitor, certidão, contrato, etc.). e extrair o máximo de informações do documento. Me retornar um json com o nome e o tipo. Exemplo: {Nome: Value, Tipo: Value}" },
                    new { type = "image_url", image_url = new { url = base64 } }
                }
            }
        },
                max_tokens = 100
            };


            var json = JsonSerializer.Serialize(payload);
            int tokens = ContarTokensComJson(json, "gpt-4-turbo"); // Contar tokens para o payload (opcional, mas útil para debug)

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Erro OpenAI:");
                Console.WriteLine("Status: " + response.StatusCode);
                Console.WriteLine("Detalhes: " + responseString);
                return $"Erro ao chamar OpenAI: {response.StatusCode}";
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseString);
                var result = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return result?.Trim() ?? "Tipo não identificado.";
            }
            catch
            {
                return "Erro ao interpretar resposta da OpenAI.";
            }
        }

        public async Task<string> AnalisarComDepSeek(IFormFile file)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _deepSeekKey);

            using var content = new MultipartFormDataContent();
            try
            {
                content.Add(new StreamContent(file.OpenReadStream()), "file", file.FileName);

                var response = await client.PostAsync("https://api.deepseek.com/v1/document/analyze", content);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"ERRO: {response.StatusCode} - {responseString}");
                    return $"Erro na API DeepSeek. Detalhes: {responseString}";
                }

                return responseString;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exceção: {ex.Message}");
                return $"Falha ao chamar a API: {ex.Message}";
            }
        }

        private static int ContarTokens(string texto, string modelo = "gpt-4-turbo")
        {
            var encoding = GptEncoding.GetEncodingForModel(modelo); // ou "cl100k_base" direto
            return encoding.Encode(texto).Count;
        }


        private static byte[] BitmapToBytes(Bitmap bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }

        public static int ContarTokensComJson(string jsonPayload, string modelo = "gpt-4-turbo")
        {
            var encoding = GptEncoding.GetEncodingForModel(modelo);
            int totalTokens = 0;

            using var doc = JsonDocument.Parse(jsonPayload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages))
            {
                foreach (var message in messages.EnumerateArray())
                {
                    totalTokens += 3; // base por mensagem

                    if (message.TryGetProperty("role", out var role))
                        totalTokens += encoding.Encode(role.GetString()).Count;

                    if (message.TryGetProperty("content", out var contentArray) && contentArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in contentArray.EnumerateArray())
                        {
                            if (item.TryGetProperty("type", out var type))
                            {
                                if (type.GetString() == "text" && item.TryGetProperty("text", out var text))
                                {
                                    totalTokens += encoding.Encode(text.GetString()).Count;
                                }
                                else if (type.GetString() == "image_url" &&
                                         item.TryGetProperty("image_url", out var imageUrl) &&
                                         imageUrl.TryGetProperty("url", out var url))
                                {
                                    totalTokens += encoding.Encode(url.GetString()).Count;
                                }
                            }
                        }
                    }
                }
            }

            totalTokens += 3; // fim da mensagem

            return totalTokens;
        }


    }
}
