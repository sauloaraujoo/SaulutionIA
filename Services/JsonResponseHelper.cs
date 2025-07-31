using System.Text.Json;

namespace SaulutionIA.Services
{
    public static class JsonResponseHelper
    {
        public static object ProcessarRespostaJson(string resposta)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(resposta))
                    return new { erro = "Resposta vazia da API" };

                // Remover markdown e caracteres de escape
                string jsonLimpo = resposta
                    .Replace("```json", "")
                    .Replace("```", "")
                    .Replace("\\n", "")
                    .Replace("\\\"", "\"")
                    .Replace("\n", "")
                    .Trim();

                // Encontrar o JSON na resposta
                int inicioJson = jsonLimpo.IndexOf('{');
                int fimJson = jsonLimpo.LastIndexOf('}');

                if (inicioJson >= 0 && fimJson > inicioJson)
                {
                    jsonLimpo = jsonLimpo.Substring(inicioJson, fimJson - inicioJson + 1);
                }

                // Tentar parsear como JSON e retornar objeto
                try
                {
                    return JsonSerializer.Deserialize<object>(jsonLimpo);
                }
                catch
                {
                    // Se falhar, tentar limpezas adicionais
                    jsonLimpo = jsonLimpo.Replace("\\", "");
                    try
                    {
                        return JsonSerializer.Deserialize<object>(jsonLimpo);
                    }
                    catch
                    {
                        // Se ainda falhar, retornar estrutura básica
                        return new 
                        { 
                            tipo_documento = "Não identificado",
                            analise_textual = resposta.Trim(),
                            observacoes = "Resposta da IA não estava em formato JSON válido",
                            timestamp = DateTime.UtcNow
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new 
                { 
                    erro = $"Erro ao processar resposta: {ex.Message}",
                    resposta_original = resposta 
                };
            }
        }
    }
}