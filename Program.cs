using Amazon;
using Amazon.Bedrock;
using Amazon.Bedrock.Model;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Amazon.Runtime.Internal.Endpoints.StandardLibrary;
using System;
using System.Dynamic;
using System.Numerics;
using System.Reflection;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;



public class Program
{
    public static async Task Main(string[] args)
    {

        //1.Basic text inference — Converse API(recommended approach)
        //The Converse API is the preferred way to interact with Bedrock models in .NET — 
        //it provides a consistent interface across all foundation models.


        // from env vars or configuration (keep secrets out of source code)
        // In production, use secure methods to manage credentials (e.g. IAM roles, AWS Secrets Manager)
        // For local testing, you can set these environment variables or use the AWS CLI to configure your credentials.
        // Example of setting env vars in Windows Command Prompt:
        // setx AWS_ACCESS_KEY_ID "your_access_key_id"
        // setx AWS_SECRET_ACCESS_KEY "your_secret_access_key"
        // setx AWS_SESSION_TOKEN "your_session_token" (optional)
        var accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        var secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        var sessionToken = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN"); // optional

        AWSCredentials creds;
        if (!string.IsNullOrEmpty(sessionToken))
            creds = new SessionAWSCredentials(accessKey, secretKey, sessionToken);
        else
            creds = new BasicAWSCredentials(accessKey, secretKey);

        var client1 = new AmazonBedrockRuntimeClient(creds, RegionEndpoint.USEast1);

        //var client = new AmazonBedrockRuntimeClient(RegionEndpoint.USEast1);

        var request1 = new ConverseRequest
        {
            ModelId = "anthropic.claude-3-haiku-20240307-v1:0",
            Messages = new List<Message>
            {
                new Message
                {
                    Role = ConversationRole.User,
                    Content = new List<ContentBlock>
                    {
                        new ContentBlock { Text = "Explain microservices in 2 sentences." }
                    }
                }
            },
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = 512,
                Temperature = 0.5F,
                TopP = 0.9F
            }
        };

        var response1 = await client1.ConverseAsync(request1);
        string result1 = response1?.Output?.Message?.Content?[0]?.Text ?? "";
        Console.WriteLine(result1);

        Console.ReadLine();


        //2.Streaming response(real - time token output)
        //The SDK supports a streaming API that returns data in chunks without waiting for the entire result — 
        //useful for displaying LLM responses as they are generated.


        var client2 = new AmazonBedrockRuntimeClient(RegionEndpoint.USEast1);

        var request2 = new ConverseStreamRequest
        {
            ModelId = "amazon.nova-lite-v1:0",
            Messages = new List<Message>
            {
                new Message
                {
                    Role = ConversationRole.User,
                    Content = new List<ContentBlock>
                    {
                        new ContentBlock { Text = "Write a short poem about cloud architecture." }
                    }
                }
            },
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = 512,
                Temperature = 0.7F
            }
        };

        var response2 = await client2.ConverseStreamAsync(request2);

        await foreach (var chunk in response2.Stream)
        {
            if (chunk is ContentBlockDeltaEvent deltaEvent)
            {
                Console.Write(deltaEvent.Delta?.Text);
            }
        }
        Console.WriteLine();


        //3.Multi - turn conversation(chat history)

       
        var client3 = new AmazonBedrockRuntimeClient(RegionEndpoint.USEast1);
        var conversation = new List<Message>();

        async Task<string> ChatAsync(string userInput)
        {
            conversation.Add(new Message
            {
                Role = ConversationRole.User,
                Content = new List<ContentBlock> { new ContentBlock { Text = userInput } }
            });

            var request3 = new ConverseRequest
            {
                ModelId = "anthropic.claude-3-haiku-20240307-v1:0",
                System = new List<SystemContentBlock>
                {
                    new SystemContentBlock { Text = "You are a helpful technical assistant." }
                },
                Messages = conversation,
                InferenceConfig = new InferenceConfiguration { MaxTokens = 1024 }
            };

            var response3 = await client3.ConverseAsync(request3);
            var assistantText = response3.Output.Message.Content[0].Text;

            // Add assistant reply to history
            conversation.Add(new Message
            {
                Role = ConversationRole.Assistant,
                Content = new List<ContentBlock> { new ContentBlock { Text = assistantText } }
            });

            return assistantText;
        }

        // Usage
        Console.WriteLine(await ChatAsync("What is Kafka?"));
        Console.WriteLine(await ChatAsync("How does it handle exactly-once semantics?"));
        Console.WriteLine(await ChatAsync("Give me a C# producer example."));



        //4.Tool use / function calling

        var client4 = new AmazonBedrockRuntimeClient(RegionEndpoint.USEast1);

        // Define a tool (e.g. get flight status)
        var flightTool = new Tool
        {
            ToolSpec = new ToolSpecification
            {
                Name = "get_flight_status",
                Description = "Get real-time status for a flight number.",
                InputSchema = new ToolInputSchema
                {
                    Json = Amazon.Runtime.Documents.Document.FromObject(new
                    {
                        type = "object",
                        properties = new
                        {
                            flight_number = new { type = "string", description = "The flight number e.g. UA123" }
                        },
                        required = new[] { "flight_number" }
                    })
                }
            }
        };

        var request4 = new ConverseRequest
        {
            ModelId = "anthropic.claude-3-haiku-20240307-v1:0",
            Messages = new List<Message>
            {
                new Message
                {
                    Role = ConversationRole.User,
                    Content = new List<ContentBlock>
                    {
                        new ContentBlock { Text = "What is the status of flight UA456?" }
                    }
                }
            },
            ToolConfig = new ToolConfiguration
            {
                Tools = new List<Tool> { flightTool }
            }
        };

        var response4 = await client4.ConverseAsync(request4);

        // Check if model wants to call a tool
        if (response4.StopReason == StopReason.Tool_use)
        {
            var toolUseBlock = response4.Output.Message.Content
                .FirstOrDefault(c => c.ToolUse != null)?.ToolUse;

            if (toolUseBlock?.Name == "get_flight_status")
            {
                // Parse the input and call your real function
                var input = toolUseBlock.Input.AsDictionary();
                var flightNumber = input["flight_number"].AsString();

                // Simulate tool result
                var toolResult = $"Flight {flightNumber} is on time, departing gate B12 at 14:35.";

                Console.WriteLine($"Tool called for: {flightNumber}");
                Console.WriteLine($"Result: {toolResult}");
            }
        }


        //5.List available foundation models
        //Use AmazonBedrockClient(control plane) to list and inspect available foundation models.AWS
        
        var client5 = new AmazonBedrockClient(RegionEndpoint.USEast1);

        var response5 = await client5.ListFoundationModelsAsync(new ListFoundationModelsRequest());

        foreach (var model in response5.ModelSummaries)
        {
            Console.WriteLine($"Model: {model.ModelId}");
            Console.WriteLine($"  Provider: {model.ProviderName}");
            Console.WriteLine($"  Input:    {string.Join(", ", model.InputModalities)}");
            Console.WriteLine($"  Output:   {string.Join(", ", model.OutputModalities)}");
            Console.WriteLine();
        }


        //6.Native API(InvokeModel) — for models not on Converse API

        var client6 = new AmazonBedrockRuntimeClient(RegionEndpoint.USEast1);

        // Claude-specific native payload
        var payload = JsonSerializer.Serialize(new
        {
            anthropic_version = "bedrock-2023-05-31",
            max_tokens = 512,
            temperature = 0.5,
            messages = new[]
            {
        new { role = "user", content = "What is the CAP theorem?" }
    }
        });

        var request6 = new InvokeModelRequest
        {
            ModelId = "anthropic.claude-3-haiku-20240307-v1:0",
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(payload))
        };

        var response6 = await client6.InvokeModelAsync(request6);

        using var reader = new StreamReader(response6.Body);
        var responseJson = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(responseJson);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        Console.WriteLine(text);
    }
}


//Environment.GetEnvironmentVariable("AWS_REGION")
//Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
//Environment.GetEnvironmentVariable("AWS_PROFILE")
//Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")
//Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY")
//Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN")
//Environment.GetEnvironmentVariable("AWS_WEB_IDENTITY_TOKEN_FILE")
//Environment.GetEnvironmentVariable("AWS_ROLE_ARN")

