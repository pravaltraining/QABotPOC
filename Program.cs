using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using UglyToad.PdfPig;
using Xceed.Words.NET;

namespace QABotPOC
{
    class Program
    {
        static async Task Main()
        {
            Console.WriteLine("Choose Assistant Mode:");
            Console.WriteLine("1. Use Azure Assistant");
            Console.WriteLine("2. Use Created Assistant");
            Console.WriteLine("3. Use Azure Chat");
            Console.Write("Enter choice: ");
            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1":
                    await UseAzureAssistant(); 
                    break;
                case "2":
                    await UseCreatedAssistant();
                    break;
                case "3":
                    await UseAzureChat();
                    break;
                default:
                    Console.WriteLine("Invalid choice. Exiting.");
                    break;
            }
        }

        static async Task UseAzureChat()
        {
            while (true)
            {
                Console.Write("> ");
                var userInput = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(userInput)) continue;
                if (userInput.Trim().ToLower() == "exit") break;


                Console.WriteLine( await AskChat(userInput) );
            }
        }

        static async Task<string> AskChat(string question)
        {
            
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("api-key", AppSettings.ApiKey);

            var requestBody = new
            {
                data_sources = new[]
                {
                    new
                    {
                        type = "azure_search",
                        parameters = new
                        {
                            endpoint = AppSettings.SearchEndPoint,
                            index_name = AppSettings.SearchIndex,
                            semantic_configuration = "default",
                            query_type = "vector_semantic_hybrid",
                            fields_mapping = new { },
                            in_scope = true,
                            filter = (Object)null,
                            strictness = 3,
                            top_n_documents = 5,
                            authentication = new
                            {
                                type = "api_key",
                                key = AppSettings.SearchKey
                            },
                            embedding_dependency = new
                            {
                                type = "deployment_name",
                                deployment_name = "text-embedding-ada-002"
                            },
                            key = AppSettings.SearchKey
                        }
                    }
                },
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "You are an AI assistant that helps people find information. Do not mention any document reference file names (like [doc1], [doc2], etc.) in your response. Provide only the necessary information to answer the user's question."
                    },
                    new
                    {
                        role = "user",
                        content = $"{question}"
                    }
                },
            temperature = 0.7,
                top_p = 0.95,
                max_tokens = 4000,
                stop = (Object)null,
                stream = false,
                frequency_penalty = 0,
                presence_penalty = 0
            };

            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

            var url = $"{AppSettings.Endpoint}openai/deployments/gpt-4.1-nano/chat/completions?api-version=2025-01-01-preview";

            var response = await httpClient.PostAsync(url, content);
            var json = await response.Content.ReadAsStringAsync();

            var result = JObject.Parse(json);
            return result["choices"]?[0]?["message"]?["content"]?.ToString().Trim() ?? "No response";
        }

        static async Task UseAzureAssistant()
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("api-key", AppSettings.ApiKey);
            var baseUrl = AppSettings.Endpoint.TrimEnd('/');

            // 1. Create a new thread
            Console.WriteLine("Creating a new assistant thread...");
            var createThreadResponse = await httpClient.PostAsync(
                $"{baseUrl}/openai/threads?api-version=2024-05-01-preview",
                new StringContent("", Encoding.UTF8, "application/json")
            );

            var threadJson = await createThreadResponse.Content.ReadAsStringAsync();
            var threadId = JObject.Parse(threadJson)["id"]?.ToString();

            if (string.IsNullOrEmpty(threadId))
            {
                Console.WriteLine("Failed to create thread.");
                return;
            }

            Console.WriteLine($"Thread created: {threadId}");

            Console.WriteLine("Start chatting with Azure Assistant (type 'exit' to end):");

            while (true)
            {
                Console.Write("> ");
                var userInput = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(userInput)) continue;
                if (userInput.Trim().ToLower() == "exit") break;

                // 2. Add message to thread
                var messagePayload = new
                {
                    role = "user",
                    content = userInput
                };

                var messageContent = new StringContent(
                    JsonConvert.SerializeObject(messagePayload),
                    Encoding.UTF8,
                    "application/json"
                );

                var addMessageResponse = await httpClient.PostAsync(
                    $"{baseUrl}/openai/threads/{threadId}/messages?api-version=2024-05-01-preview",
                    messageContent
                );

                var messageJson = await addMessageResponse.Content.ReadAsStringAsync();
                if (!addMessageResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine("Error sending message.");
                    Console.WriteLine(messageJson);
                    continue;
                }

                // 3. Run the thread
                var runPayload = new
                {
                    assistant_id = AppSettings.AssistantId
                };

                var runContent = new StringContent(
                    JsonConvert.SerializeObject(runPayload),
                    Encoding.UTF8,
                    "application/json"
                );

                var runResponse = await httpClient.PostAsync(
                    $"{baseUrl}/openai/threads/{threadId}/runs?api-version=2024-05-01-preview",
                    runContent
                );

                var runJson = await runResponse.Content.ReadAsStringAsync();
                var runId = JObject.Parse(runJson)["id"]?.ToString();

                if (string.IsNullOrEmpty(runId))
                {
                    Console.WriteLine("Failed to start run.");
                    continue;
                }

                // 4. Wait for run to complete
                string status = "";
                int retry = 0;

                do
                {
                    await Task.Delay(1000); // wait a bit before checking
                    var statusResponse = await httpClient.GetAsync(
                        $"{baseUrl}/openai/threads/{threadId}/runs/{runId}?api-version=2024-05-01-preview"
                    );
                    var statusJson = await statusResponse.Content.ReadAsStringAsync();
                    var statusObj = JObject.Parse(statusJson);
                    status = statusObj["status"]?.ToString();
                    retry++;

                } while (status != "completed" && retry < 10);

                if (status != "completed")
                {
                    Console.WriteLine("Run timed out or failed.");
                    continue;
                }

                // 5. Get assistant's response
                var getMessagesResponse = await httpClient.GetAsync(
                    $"{baseUrl}/openai/threads/{threadId}/messages?api-version=2024-05-01-preview"
                );

                var messagesJson = await getMessagesResponse.Content.ReadAsStringAsync();
                var messages = JObject.Parse(messagesJson)["data"];

                var latestAssistantMessage = messages
                    .FirstOrDefault(m => m["role"]?.ToString() == "assistant")?["content"]?[0]?["text"]?["value"]?.ToString();

                Console.WriteLine($"\n\tAssistant: {latestAssistantMessage}\n");
            }

            Console.WriteLine("Chat ended. (Thread not deleted; you can add deletion logic here if needed.)");
        }

        static async Task UseCreatedAssistant()
        {
            var vectorStore = new VectorStore();
            var docsPath = Path.Combine(Directory.GetCurrentDirectory(), "Documents");

            Console.WriteLine("Reading documents...");

            foreach (var file in Directory.GetFiles(docsPath))
            {
                string content = "";

                if (file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    content = await File.ReadAllTextAsync(file);
                }
                else if (file.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    content = ExtractTextFromDocx(file);
                }
                else if (file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    content = ExtractTextFromPdf(file);
                }
                else
                {
                    Console.WriteLine($"Unsupported file format: {file}");
                    continue;
                }

                var chunks = ChunkText(content);

                foreach (var chunk in chunks)
                {
                    var embedding = await EmbeddingHelper.GetEmbeddingAsync(chunk);
                    vectorStore.AddChunk(chunk, embedding);
                }
            }

            Console.WriteLine("Ready. Ask a question:");

            while (true)
            {
                Console.Write("> ");
                var question = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(question)) continue;
                if (question.ToLower() == "exit") break;

                var questionEmbedding = await EmbeddingHelper.GetEmbeddingAsync(question);
                var bestChunk = vectorStore.FindBestMatch(questionEmbedding);

                var prompt = $"""
                You are a helpful assistant. Use the context below to answer the user's question.

                Context:
                {bestChunk.Text}

                Question:
                {question}

                Answer:
                """;

                var answer = await AskOpenAI(prompt);
                Console.WriteLine($"\n\t{answer}\n");
            }
        }

        static string[] ChunkText(string content, int chunkSize = 200)
        {
            var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var chunks = new List<string>();
            for (int i = 0; i < words.Length; i += chunkSize)
            {
                var chunk = string.Join(" ", words.Skip(i).Take(chunkSize));
                chunks.Add(chunk);
            }
            return chunks.ToArray();
        }

        public static async Task<string> AskOpenAI(string userPrompt)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("api-key", AppSettings.ApiKey);

            var requestBody = new
            {
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                },
            };

            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

            var url = $"{AppSettings.Endpoint}openai/deployments/{AppSettings.ChatDeployment}/chat/completions?api-version=2025-01-01-preview";

            var response = await httpClient.PostAsync(url, content);
            var json = await response.Content.ReadAsStringAsync();

            var result = JObject.Parse(json);
            return result["choices"]?[0]?["message"]?["content"]?.ToString().Trim() ?? "No response";
        }

        public static string ExtractTextFromDocx(string filePath)
        {
            using var doc = DocX.Load(filePath);
            return doc.Text;
        }

        public static string ExtractTextFromPdf(string filePath)
        {
            var text = new StringBuilder();
            using var document = PdfDocument.Open(filePath);
            foreach (var page in document.GetPages())
            {
                text.AppendLine(page.Text);
            }
            return text.ToString();
        }
    }
}