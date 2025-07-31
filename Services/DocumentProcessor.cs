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
        private static readonly HttpClient _httpClient = new HttpClient();

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

            // Monta o payload para a API do ChatGPT com Vision - usando o modelo mais recente
            var payload = new
            {
                model = "gpt-4o", // Modelo mais recente com melhor capacidade de visão
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "Analise este documento e identifique o tipo (ex: CNH, RG, título de eleitor, certidão, contrato, etc.). Extraia o máximo de informações possíveis. Retorne um JSON estruturado com as informações encontradas. Exemplo: {\"tipo\": \"CNH\", \"nome\": \"João Silva\", \"numero\": \"123456789\", \"outras_informacoes\": {}}" },
                            new { type = "image_url", image_url = new { url = base64, detail = "high" } }
                        }
                    }
                },
                max_tokens = 500,
                temperature = 0.1 // Baixa temperatura para respostas mais consistentes
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Erro OpenAI:");
                    Console.WriteLine("Status: " + response.StatusCode);
                    Console.WriteLine("Detalhes: " + responseString);
                    return $"Erro ao chamar OpenAI: {response.StatusCode}";
                }

                using JsonDocument doc = JsonDocument.Parse(responseString);
                var result = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return result?.Trim() ?? "Tipo não identificado.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao chamar OpenAI: {ex.Message}");
                return $"Erro ao interpretar resposta da OpenAI: {ex.Message}";
            }
        }

        public async Task<string> AnalisarComDepSeek(IFormFile file)
        {
            try
            {
                // Primeiro, extrair o conteúdo do arquivo
                string fileContent = await ExtrairTextoOuImagemBase64(file);
                
                // DeepSeek API - Usar abordagem diferente pois DeepSeek não suporta imagens diretamente
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _deepSeekKey);
                client.Timeout = TimeSpan.FromMinutes(2); // Aumentar timeout

                // Para DeepSeek, vamos usar OCR via OpenAI primeiro e depois analisar com DeepSeek
                string textoExtraido = "";
                
                if (fileContent.StartsWith("data:image"))
                {
                    // Se for imagem, usar OpenAI apenas para OCR
                    var ocrPayload = new
                    {
                        model = "gpt-4o",
                        messages = new[]
                        {
                            new
                            {
                                role = "user",
                                content = new object[]
                                {
                                    new { type = "text", text = "Extraia TODO o texto visível nesta imagem. Retorne apenas o texto, sem formatação ou explicações." },
                                    new { type = "image_url", image_url = new { url = fileContent, detail = "high" } }
                                }
                            }
                        },
                        max_tokens = 1000,
                        temperature = 0.1
                    };

                    var ocrJson = JsonSerializer.Serialize(ocrPayload);
                    var ocrContent = new StringContent(ocrJson, Encoding.UTF8, "application/json");

                    using var ocrClient = new HttpClient();
                    ocrClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiKey);
                    
                    var ocrResponse = await ocrClient.PostAsync("https://api.openai.com/v1/chat/completions", ocrContent);
                    if (ocrResponse.IsSuccessStatusCode)
                    {
                        var ocrResponseString = await ocrResponse.Content.ReadAsStringAsync();
                        using JsonDocument ocrDoc = JsonDocument.Parse(ocrResponseString);
                        textoExtraido = ocrDoc.RootElement
                            .GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString() ?? "";
                    }
                    else
                    {
                        Console.WriteLine($"Erro OCR OpenAI: {ocrResponse.StatusCode}");
                        textoExtraido = "Não foi possível extrair texto da imagem";
                    }
                }
                else
                {
                    textoExtraido = fileContent;
                }

                // Agora usar DeepSeek para analisar o texto extraído
                var payload = new
                {
                    model = "deepseek-chat",
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = $@"Analise o seguinte texto de documento e extraia TODAS as informações estruturadas.

TEXTO DO DOCUMENTO:
{textoExtraido}

Identifique o tipo de documento (CNH, RG, CPF, certidão, certificado, contrato, etc.) e extraia todas as informações relevantes como:
- Nomes completos
- Números de documentos  
- Datas (nascimento, expedição, validade, etc.)
- Endereços
- Órgãos emissores
- Qualquer outra informação relevante

IMPORTANTE: Retorne APENAS um JSON válido estruturado, sem markdown, sem explicações.
Exemplo: {{""tipo_documento"": ""CNH"", ""nome_completo"": ""João Silva"", ""numero_registro"": ""123456789"", ""data_nascimento"": ""01/01/1990"", ""validade"": ""01/01/2030"", ""categoria"": ""AB""}}"
                        }
                    },
                    max_tokens = 1200,
                    temperature = 0.1,
                    stream = false
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Console.WriteLine($"Tentando chamar DeepSeek API...");
                var response = await client.PostAsync("https://api.deepseek.com/v1/chat/completions", content);
                var responseString = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"DeepSeek Response Status: {response.StatusCode}");
                Console.WriteLine($"DeepSeek Response: {responseString}");

                if (!response.IsSuccessStatusCode)
                {
                    // Se DeepSeek falhar, usar análise com OpenAI como fallback
                    Console.WriteLine($"DeepSeek falhou, usando OpenAI como fallback...");
                    return await AnalisarComOpenAIFallback(textoExtraido);
                }

                using JsonDocument doc = JsonDocument.Parse(responseString);
                var result = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return result?.Trim() ?? "Análise não disponível.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exceção DeepSeek: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                
                // Fallback para OpenAI se DeepSeek falhar completamente
                try
                {
                    var fileContent = await ExtrairTextoOuImagemBase64(file);
                    if (fileContent.StartsWith("data:image"))
                    {
                        return await IdentificarTipoDocumento(fileContent);
                    }
                    else
                    {
                        return await AnalisarComOpenAIFallback(fileContent);
                    }
                }
                catch
                {
                    return await AnalisarDocumentoFallback(file);
                }
            }
        }

        // Método auxiliar para análise com OpenAI quando DeepSeek falha
        private async Task<string> AnalisarComOpenAIFallback(string textoExtraido)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiKey);

                var payload = new
                {
                    model = "gpt-4o",
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = $@"Analise o seguinte texto de documento e extraia TODAS as informações estruturadas.

TEXTO DO DOCUMENTO:
{textoExtraido}

Identifique o tipo de documento e extraia todas as informações relevantes.
IMPORTANTE: Retorne APENAS um JSON válido estruturado, sem markdown, sem explicações.
Exemplo: {{""tipo_documento"": ""CNH"", ""nome_completo"": ""João Silva"", ""numero_registro"": ""123456789""}}"
                        }
                    },
                    max_tokens = 800,
                    temperature = 0.1
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return $"{{\"erro\": \"Falha no fallback OpenAI: {response.StatusCode}\"}}";
                }

                using JsonDocument doc = JsonDocument.Parse(responseString);
                var result = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return result?.Trim() ?? "Análise não disponível.";
            }
            catch (Exception ex)
            {
                return $"{{\"erro\": \"Exceção no fallback OpenAI: {ex.Message}\"}}";
            }
        }

        public async Task<string> AnalisarDocumentoFallback(IFormFile file)
        {
            try
            {
                // Fallback usando análise local básica
                var extensao = Path.GetExtension(file.FileName).ToLower();
                var tamanho = file.Length;
                var tipo = file.ContentType;

                var analiseBasica = new
                {
                    tipo_arquivo = tipo,
                    extensao = extensao,
                    tamanho_bytes = tamanho,
                    nome_arquivo = file.FileName,
                    analise = "Análise básica - API DeepSeek indisponível",
                    timestamp = DateTime.UtcNow
                };

                return JsonSerializer.Serialize(analiseBasica, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }
            catch (Exception ex)
            {
                return $"{{\"erro\": \"Falha na análise de fallback: {ex.Message}\"}}";
            }
        }

        // Método melhorado para análise combinada usando ambas as APIs
        public async Task<string> AnalisarDocumentoCompleto(IFormFile file)
        {
            try
            {
                var resultados = new Dictionary<string, object>();

                // Análise com OpenAI
                try
                {
                    var base64Content = await ExtrairTextoOuImagemBase64(file);
                    var resultadoOpenAI = await IdentificarTipoDocumento(base64Content);
                    resultados["openai_analysis"] = resultadoOpenAI;
                }
                catch (Exception ex)
                {
                    resultados["openai_analysis"] = $"Erro: {ex.Message}";
                }

                // Análise com DeepSeek
                try
                {
                    var resultadoDeepSeek = await AnalisarComDepSeek(file);
                    resultados["deepseek_analysis"] = resultadoDeepSeek;
                }
                catch (Exception ex)
                {
                    resultados["deepseek_analysis"] = $"Erro: {ex.Message}";
                }

                resultados["file_info"] = new
                {
                    nome = file.FileName,
                    tipo = file.ContentType,
                    tamanho = file.Length,
                    timestamp = DateTime.UtcNow
                };

                return JsonSerializer.Serialize(resultados, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }
            catch (Exception ex)
            {
                return $"{{\"erro\": \"Falha na análise completa: {ex.Message}\"}}";
            }
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

        // Método para debug - verificar se DeepSeek está funcionando
        public async Task<string> TestarDeepSeek()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _deepSeekKey);

                var payload = new
                {
                    model = "deepseek-chat",
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = "Teste simples. Responda apenas: 'DeepSeek funcionando'"
                        }
                    },
                    max_tokens = 50
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://api.deepseek.com/v1/chat/completions", content);
                var responseString = await response.Content.ReadAsStringAsync();

                return $"Status: {response.StatusCode}, Response: {responseString}";
            }
            catch (Exception ex)
            {
                return $"Erro: {ex.Message}";
            }
        }
    }
}
