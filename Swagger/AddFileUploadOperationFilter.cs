using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SaulutionIA.Swagger
{
    public class AddFileUploadOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var fileParams = context.MethodInfo
                .GetParameters()
                .Where(p => p.ParameterType == typeof(IFormFile));

            if (!fileParams.Any())
                return;

            operation.RequestBody = new OpenApiRequestBody
            {
                Content = {
                    ["multipart/form-data"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = {
                                ["documento"] = new OpenApiSchema
                                {
                                    Description = "Arquivo a ser enviado",
                                    Type = "string",
                                    Format = "binary"
                                }
                            },
                            Required = { "documento" }
                        }
                    }
                }
            };
        }
    }
}
