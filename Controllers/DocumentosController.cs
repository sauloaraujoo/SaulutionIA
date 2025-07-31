using Microsoft.AspNetCore.Mvc;
using SaulutionIA.Models;
using SaulutionIA.Services;

namespace SaulutionIA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentosController : ControllerBase
    {
        private readonly DocumentProcessor _processor;

        public DocumentosController(DocumentProcessor processor)
        {
            _processor = processor;
        }

        [HttpPost("analisar-chatpt")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> AnalisarDocumento([FromForm] AnalisarDocumentoRequest request)
        {
            var documento = request.Documento;

            if (documento == null || documento.Length == 0)
                return BadRequest("Arquivo não enviado.");

            try
            {
                var input = await _processor.ExtrairTextoOuImagemBase64(documento);  
                var resultado = await _processor.IdentificarTipoDocumento(input);

                var jsonLimpo = JsonResponseHelper.ProcessarRespostaJson(resultado);
                return Ok(jsonLimpo);
            }
            catch (Exception ex)
            {
                return Problem($"Erro ao processar documento: {ex.Message}");
            }
        }

        [HttpPost("analisar-deepseek")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> AnalisarComDeepSeek([FromForm] AnalisarDocumentoRequest request)
        {
            var documento = request.Documento;

            if (documento == null || documento.Length == 0)
                return BadRequest("Arquivo não enviado.");

            try
            {
                var resultado = await _processor.AnalisarComDepSeek(documento);
                var jsonLimpo = JsonResponseHelper.ProcessarRespostaJson(resultado);
                return Ok(jsonLimpo);
            }
            catch (Exception ex)
            {
                return Problem($"Erro ao processar documento com DeepSeek: {ex.Message}");
            }
        }

        //[HttpPost("analisar-completo")]
        //[Consumes("multipart/form-data")]
        //public async Task<IActionResult> AnalisarDocumentoCompleto([FromForm] AnalisarDocumentoRequest request)
        //{
        //    var documento = request.Documento;

        //    if (documento == null || documento.Length == 0)
        //        return BadRequest("Arquivo não enviado.");

        //    try
        //    {
        //        var resultado = await _processor.AnalisarDocumentoCompleto(documento);
        //        return Ok(new { analise_completa = resultado });
        //    }
        //    catch (Exception ex)
        //    {
        //        return Problem($"Erro ao processar documento completo: {ex.Message}");
        //    }
        //}
    }
}
