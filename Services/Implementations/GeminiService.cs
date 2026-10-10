using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Fleetify.Models.Entities;
using Fleetify.Services.Interfaces;

namespace Fleetify.Services.Implementations
{
    public class GeminiService : IGeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GeminiService> _logger;
        private readonly string? _apiKey;
        private readonly string _model;

        public GeminiService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            // Prioritize configuration, fallback to environment variable
            _apiKey = configuration["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.Equals("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase))
            {
                _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            }

            var cfgModel = configuration["Gemini:Model"];
            if (string.IsNullOrWhiteSpace(cfgModel) || cfgModel.Equals("gemini-3.5-flash-lite", StringComparison.OrdinalIgnoreCase))
            {
                _model = "gemini-1.5-flash";
            }
            else
            {
                _model = cfgModel;
            }
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey) && !_apiKey.Equals("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase);

        public async Task<string?> GenerateReplyAsync(string userMessage, List<SupportMessage>? conversationHistory = null, string? liveContext = null)
        {
            if (!IsConfigured)
            {
                return null;
            }

            // List of models to try in case of deprecation or 404
            var modelsToTry = new List<string> { _model };
            if (!_model.Equals("gemini-1.5-flash", StringComparison.OrdinalIgnoreCase)) modelsToTry.Add("gemini-1.5-flash");
            if (!_model.Equals("gemini-2.0-flash", StringComparison.OrdinalIgnoreCase)) modelsToTry.Add("gemini-2.0-flash");
            if (!_model.Equals("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase)) modelsToTry.Add("gemini-2.5-flash");

            foreach (var currentModel in modelsToTry)
            {
                var result = await TryGenerateContentAsync(currentModel, userMessage, conversationHistory, liveContext);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result;
                }
            }

            return null;
        }

        private async Task<string?> TryGenerateContentAsync(string modelName, string userMessage, List<SupportMessage>? conversationHistory, string? liveContext)
        {
            try
            {
                string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={_apiKey}";

                // Build System Instructions
                var systemInstructionText = new StringBuilder();
                systemInstructionText.AppendLine("You are Fleetify AI, an intelligent, empathetic virtual support assistant for Fleetify Logistics & Fleet Management Platform.");
                systemInstructionText.AppendLine("Fleetify Information & Knowledge Base:");
                systemInstructionText.AppendLine("• Services: Door-to-door delivery, B2B & B2C cargo, on-demand dispatch, and real-time fleet management.");
                systemInstructionText.AppendLine("• Fleet Vehicle Types & Capacity Limits:");
                systemInstructionText.AppendLine("  - Bike: Up to 40 kg (documents, envelopes, small packages)");
                systemInstructionText.AppendLine("  - Pickup: Up to 800 kg (tools, hardware, medium cargo, appliances)");
                systemInstructionText.AppendLine("  - Van: Up to 1,500 kg (boxes, furniture, medium cartons)");
                systemInstructionText.AppendLine("  - Truck: Up to 5,000 kg (industrial pallets, bulk construction, heavy cargo)");
                systemInstructionText.AppendLine("• Pricing Calculation Model & Exact Formula:");
                systemInstructionText.AppendLine("  - Currency: Rs. (Pakistani Rupees)");
                systemInstructionText.AppendLine("  - Distance Tier Charge (IFS tiers based on Distance in km):");
                systemInstructionText.AppendLine("    • Distance < 50 km: Rs. 1,500");
                systemInstructionText.AppendLine("    • Distance < 150 km: Rs. 2,500");
                systemInstructionText.AppendLine("    • Distance < 250 km: Rs. 4,000");
                systemInstructionText.AppendLine("    • Distance < 500 km: Rs. 7,000");
                systemInstructionText.AppendLine("    • Distance < 1,000 km: Rs. 12,000");
                systemInstructionText.AppendLine("    • Distance < 1,500 km (and above): Rs. 18,000");
                systemInstructionText.AppendLine("  - Weight Calculation: Total Weight (B2) = Single Item Weight (kg) × Quantity.");
                systemInstructionText.AppendLine("  - Weight Charge: Rs. 2 per kg (2 × B2)");
                systemInstructionText.AppendLine("  - Delivery Types & Multipliers (C2):");
                systemInstructionText.AppendLine("    • Normal Delivery: Multiplier 1.0 (4–6 business days)");
                systemInstructionText.AppendLine("    • Fast Delivery: Multiplier 1.75 (2–3 business days)");
                systemInstructionText.AppendLine("    • Express Delivery: Multiplier 2.5 (1–2 business days)");
                systemInstructionText.AppendLine("  - Total Volume in Cubic Feet (D2) = (Length_ft × Width_ft × Height_ft) × Quantity.");
                systemInstructionText.AppendLine("  - Volume Multipliers (IFS on D2 in CFT):");
                systemInstructionText.AppendLine("    • D2 < 1000 CFT: Multiplier 1.0");
                systemInstructionText.AppendLine("    • D2 < 2000 CFT: Multiplier 1.25");
                systemInstructionText.AppendLine("    • D2 < 3000 CFT: Multiplier 1.40");
                systemInstructionText.AppendLine("    • D2 >= 3000 CFT: Multiplier 1.50");
                systemInstructionText.AppendLine("  - Rounding Rule: All decimal numbers are rounded down / floored to the previous whole number.");
                systemInstructionText.AppendLine("  - Exact Formula: Total Cost = Math.floor(( (2 × B2) + Distance_Tier_Charge(A2) ) × Delivery_Multiplier(C2) × Volume_Multiplier(D2))");
                systemInstructionText.AppendLine("  - Example: Distance = 104 km (A2 < 150 -> Rs. 2500), Single Wt = 2.5 kg, Qty = 4 -> Total Weight B2 = 10 kg (Fee = 2 × 10 = Rs. 20), CFT = 1200 (<2000 -> 1.25×), Normal Delivery (1.0×) -> Total = ((20 + 2500) × 1.0 × 1.25) = Rs. 3150.");
                systemInstructionText.AppendLine("• Booking Flow: Customers log in to Customer Portal -> Click 'New Delivery Request' -> specify pickup, drop-off, weight, and vehicle -> confirm instant AI estimate -> Admin assigns nearest available driver.");
                systemInstructionText.AppendLine("• Tracking Format: Valid tracking codes start with 'FLT-2026-' followed by 6 digits (e.g., FLT-2026-302107).");
                systemInstructionText.AppendLine();
                systemInstructionText.AppendLine("Behavioral Guidelines:");
                systemInstructionText.AppendLine("1. Respond in the same language the customer uses (English, Urdu, or Roman Urdu).");
                systemInstructionText.AppendLine("2. Keep replies conversational, clear, helpful, and formatted with bullet points where appropriate.");
                systemInstructionText.AppendLine("3. If live database context is provided below, trust and use it as accurate real-time truth.");
                systemInstructionText.AppendLine("4. Escalation Trigger: If the customer expresses frustration, reports damaged or missing goods, demands a refund, or explicitly requests to speak with a human agent or manager, provide an empathetic answer and append '[ESCALATE_TO_ADMIN]' at the very end of your response so our system automatically assigns an administrator.");

                if (!string.IsNullOrWhiteSpace(liveContext))
                {
                    systemInstructionText.AppendLine();
                    systemInstructionText.AppendLine("=== LIVE SYSTEM / DATABASE CONTEXT ===");
                    systemInstructionText.AppendLine(liveContext);
                }

                // Build contents array (history + current message)
                var contents = new List<object>();

                if (conversationHistory != null && conversationHistory.Count > 0)
                {
                    // Include up to last 6 messages for context
                    var recentMessages = conversationHistory
                        .OrderBy(m => m.SentAt)
                        .TakeLast(6);

                    foreach (var msg in recentMessages)
                    {
                        string role = msg.SenderType == "User" ? "user" : "model";
                        contents.Add(new
                        {
                            role = role,
                            parts = new object[] { new { text = msg.MessageText } }
                        });
                    }
                }

                // Add latest user message
                contents.Add(new
                {
                    role = "user",
                    parts = new object[] { new { text = userMessage } }
                });

                var requestBody = new
                {
                    systemInstruction = new
                    {
                        parts = new object[] { new { text = systemInstructionText.ToString() } }
                    },
                    contents = contents,
                    generationConfig = new
                    {
                        temperature = 0.6,
                        maxOutputTokens = 600
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);
                using var requestContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync(endpoint, requestContent);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Gemini API call returned status {StatusCode}: {ResponseBody}", response.StatusCode, responseBody);
                    return null;
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0)
                    {
                        var textElement = parts[0].GetProperty("text");
                        return textElement.GetString();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while calling Google Gemini API.");
                return null;
            }
        }
    }
}
