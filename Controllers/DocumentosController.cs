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

        [HttpPost("analisar")]
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
                //var resultado = await _processor.AnalisarComDepSeek(documento);


                return Ok(new { tipo = resultado });
            }
            catch (Exception ex)
            {
                return Problem($"Erro ao processar documento: {ex.Message}");
            }
        }
    }
}
