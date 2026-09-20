using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using mdaiAgent;
using Xunit;

namespace mdaiAgent.Tests
{
    public class AttachmentSerializationTests
    {
        [Fact]
        public void SerializeChatRequest_IncludesAttachment()
        {
            var contentBytes = Encoding.UTF8.GetBytes("hello world");
            var att = new Attachment
            {
                FileName = "greeting.txt",
                MimeType = "text/plain",
                Base64Content = Convert.ToBase64String(contentBytes),
                Size = contentBytes.Length
            };

            var msg = new ExtendedChatMessage
            {
                Role = "user",
                Content = "Here is a file",
                Attachments = new List<Attachment> { att }
            };

            var req = new ChatRequest
            {
                Model = "test-model",
                Messages = new List<ExtendedChatMessage> { msg },
                Tools = null
            };

            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(req, options);

            Assert.Contains("attachments", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("greeting.txt", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Convert.ToBase64String(contentBytes), json, StringComparison.Ordinal);
        }
    }
}
