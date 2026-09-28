using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace HelloApi
{
    /// <summary>
    /// Lambda expuesta detrás de una API Gateway HTTP API.
    /// Endpoint: GET /hello?name=X
    /// Guarda cada visita en DynamoDB y devuelve un saludo + contador total de visitas.
    /// </summary>
    public class Function
    {
        // Cliente de DynamoDB reutilizado entre invocaciones (fuera del handler,
        // para aprovechar el "warm start" de Lambda y no recrearlo cada vez).
        private static readonly AmazonDynamoDBClient DynamoClient = new AmazonDynamoDBClient();

        // Nombre de la tabla, inyectado como variable de entorno desde template.yaml.
        private static readonly string TableName =
            Environment.GetEnvironmentVariable("VISITS_TABLE_NAME") ?? "Visits";

        public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
            APIGatewayHttpApiV2ProxyRequest request,
            ILambdaContext context)
        {
            try
            {
                string name = ExtractNameParameter(request);

                if (string.IsNullOrWhiteSpace(name))
                {
                    return BuildResponse(HttpStatusCode.BadRequest,
                        new { error = "El parámetro 'name' es requerido. Ejemplo: /hello?name=Ricardo" });
                }

                context.Logger.LogInformation($"Procesando saludo para: {name}");

                var visitId = Guid.NewGuid().ToString();
                var timestamp = DateTime.UtcNow.ToString("o");

                await SaveVisitAsync(visitId, name, timestamp, context);

                long totalVisits = await GetVisitCountAsync(context);

                var responseBody = new
                {
                    message = $"Hola, {name}!",
                    visitId,
                    timestamp,
                    visitCount = totalVisits
                };

                return BuildResponse(HttpStatusCode.OK, responseBody);
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error procesando la solicitud: {ex}");
                return BuildResponse(HttpStatusCode.InternalServerError,
                    new { error = "Ocurrió un error procesando la solicitud." });
            }
        }

        private string ExtractNameParameter(APIGatewayHttpApiV2ProxyRequest request)
        {
            if (request.QueryStringParameters != null &&
                request.QueryStringParameters.TryGetValue("name", out var name))
            {
                return name;
            }
            return string.Empty;
        }

        private async Task SaveVisitAsync(string visitId, string name, string timestamp, ILambdaContext context)
        {
            var putRequest = new PutItemRequest
            {
                TableName = TableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["visitId"] = new AttributeValue { S = visitId },
                    ["name"] = new AttributeValue { S = name },
                    ["timestamp"] = new AttributeValue { S = timestamp }
                }
            };

            await DynamoClient.PutItemAsync(putRequest);
            context.Logger.LogInformation($"Visita guardada en DynamoDB: {visitId}");
        }

        private async Task<long> GetVisitCountAsync(ILambdaContext context)
        {
            // Scan con Select=COUNT es aceptable para una demo/hello-world con poco tráfico.
            // En una tabla con mucho volumen, se preferiría mantener un contador agregado
            // aparte (ej. un item "counter" actualizado con UpdateItem + ADD) en vez de escanear.
            var scanRequest = new ScanRequest
            {
                TableName = TableName,
                Select = Select.COUNT
            };

            var result = await DynamoClient.ScanAsync(scanRequest);
            return result.Count;
        }

        private APIGatewayHttpApiV2ProxyResponse BuildResponse(HttpStatusCode statusCode, object body)
        {
            return new APIGatewayHttpApiV2ProxyResponse
            {
                StatusCode = (int)statusCode,
                Body = JsonSerializer.Serialize(body),
                Headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json"
                }
            };
        }
    }
}
