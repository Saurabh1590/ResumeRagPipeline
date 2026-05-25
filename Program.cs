using Azure;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using UglyToad.PdfPig;
using OpenAI.Chat;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

// ---------------------------------------------------------
// 1. CONFIGURATION & CLIENT INITIALIZATION
// ---------------------------------------------------------
var embedConfig = builder.Configuration.GetSection("AiSettings:Embedding");
var chatConfig = builder.Configuration.GetSection("AiSettings:Chat");
var qdrantConfig = builder.Configuration.GetSection("Qdrant");
var collectionName = "resume_knowledge_base";

var embedAiClient = new AzureOpenAIClient(new Uri(embedConfig["Endpoint"]!), new AzureKeyCredential(embedConfig["Key"]!));
var embeddingClient = embedAiClient.GetEmbeddingClient(embedConfig["Deployment"]!);

var chatAiClient = new AzureOpenAIClient(new Uri(chatConfig["Endpoint"]!), new AzureKeyCredential(chatConfig["Key"]!));
var chatClient = chatAiClient.GetChatClient(chatConfig["Deployment"]!);

var qdrantClient = new QdrantClient(host: qdrantConfig["Host"]!, port: 6334, https: true, apiKey: qdrantConfig["ApiKey"]!);

// ---------------------------------------------------------
// 2. HELPER FUNCTIONS
// ---------------------------------------------------------
Guid GenerateDeterministicGuid(string input)
{
    using (MD5 md5 = MD5.Create())
    {
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }
}

// ---------------------------------------------------------
// 3. INGESTION ENDPOINT (Simplified to use Filename)
// ---------------------------------------------------------
app.MapPost("/ingest-pdf", async (IFormFile file) =>
{
    if (file == null || file.Length == 0) return Results.BadRequest("No file uploaded.");

    try
    {
        using var stream = file.OpenReadStream();
        using var pdf = PdfDocument.Open(stream);
        var fullText = string.Join(" ", pdf.GetPages().Select(p => p.Text));

        // Use filename directly as requested
        var candidateName = Path.GetFileNameWithoutExtension(file.FileName);

        var chunks = new List<string>();
        for (int i = 0; i < fullText.Length; i += 700)
        {
            int length = Math.Min(800, fullText.Length - i);
            chunks.Add(fullText.Substring(i, length));
        }

        var points = new List<PointStruct>();
        for (int i = 0; i < chunks.Count; i++)
        {
            var response = await embeddingClient.GenerateEmbeddingAsync(chunks[i]);
            var vector = response.Value.ToFloats().ToArray();

            // Deterministic ID ensures re-uploads overwrite by filename
            Guid stableId = GenerateDeterministicGuid($"{file.FileName}_chunk_{i}");

            points.Add(new PointStruct
            {
                Id = stableId,
                Vectors = vector,
                Payload = {
                    { "text", chunks[i] },
                    { "candidate_name", candidateName },
                    { "filename", file.FileName },
                    { "upload_date", DateTime.UtcNow.ToString("O") }
                }
            });
        }

        await qdrantClient.UpsertAsync(collectionName, points);

        return Results.Ok(new { Message = "Success!", FileIngested = candidateName });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
}).DisableAntiforgery();

// ---------------------------------------------------------
// 4. CHAT ENDPOINT (RAG)
// ---------------------------------------------------------
app.MapGet("/ask-ai", async (string query) =>
{
    try
    {
        var responseEmbed = await embeddingClient.GenerateEmbeddingAsync(query);
        var searchResults = await qdrantClient.SearchAsync(collectionName, responseEmbed.Value.ToFloats().ToArray(), limit: 3);

        var context = string.Join("\n\n", searchResults.Select(r =>
            $"[Source File: {r.Payload["candidate_name"]}] {r.Payload["text"]}"));

        List<ChatMessage> messages = new()
        {
            new SystemChatMessage("You are an HR Assistant at ACS Infotech. Answer based on the provided context. Cite the source file name in your answer."),
            new UserChatMessage($"Context: {context}\n\nQuestion: {query}")
        };

        ChatCompletion completion = await chatClient.CompleteChatAsync(messages);

        return Results.Ok(new
        {
            Answer = completion.Content[0].Text,
            Sources = searchResults.Select(r => r.Payload["filename"].StringValue).Distinct()
        });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.Run();