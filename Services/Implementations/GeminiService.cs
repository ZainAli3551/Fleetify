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

            _model = configuration["Gemini:Model"] ?? "gemini-3.5-flash-lite";
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey) && !_apiKey.Equals("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase);

        public async Task<string?> GenerateReplyAsync(string userMessage, List<SupportMessage>? conversationHistory = null, string? liveContext = null)
        {
            if (!IsConfigured)
            {
                return null;
            }

            try
            {
                string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

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
                systemInstructionText.AppendLine("  - Distance-Based Petrol Surcharge (Included in calculation, but never displayed as a separate line item to customer):");
                systemInstructionText.AppendLine("    • Distance under 5 km: + Rs. 200");
                systemInstructionText.AppendLine("    • Distance from 5 km to under 10 km: + Rs. 300");
                systemInstructionText.AppendLine("    • Distance from 10 km to under 20 km: + Rs. 500");
                systemInstructionText.AppendLine("    • Distance from 20 km to under 50 km: + Rs. 700");
                systemInstructionText.AppendLine("    • Distance from 50 km to under 100 km: + Rs. 1,000");
                systemInstructionText.AppendLine("    • Distance from 100 km to 200 km+: + Rs. 2,500");
                systemInstructionText.AppendLine("  - Delivery Types & Multipliers:");
                systemInstructionText.AppendLine("    • Normal Delivery: Multiplier 1.0 (Standard 1-2 business days)");
                systemInstructionText.AppendLine("    • Fast Delivery: Multiplier 1.75 (Next-day priority delivery)");
                systemInstructionText.AppendLine("    • Express Delivery: Multiplier 2.5 (Same-day rush delivery)");
                systemInstructionText.AppendLine("  - Rounding Rule: All decimal/point numbers (distance, weight, and final cost) are rounded down / floored to the previous whole number (e.g., 2802.10 -> 2802, 2.535 -> 2, 100.7 km -> 100 km).");
                systemInstructionText.AppendLine("  - Final Formula: Total Cost = Math.floor(((1.5 × Distance_km × Weight_kg) + Petrol_Surcharge) × Delivery_Multiplier)");
                systemInstructionText.AppendLine("  - Example 1: Distance = 100.7 km, Weight = 2 kg, Normal Delivery -> Base = 1.5 × 100.7 × 2 = 302.10, Petrol = 2500. Total = Math.floor((302.10 + 2500) × 1.0) = Rs. 2802.");
                systemInstructionText.AppendLine("  - Example 2: Distance = 15 km, Weight = 2 kg, Fast Delivery -> Base = 1.5 × 15 × 2 = 45, Petrol = 500. Total = Math.floor((45 + 500) × 1.75) = Math.floor(953.75) = Rs. 953.");
                systemInstructionText.AppendLine("  - COST CALCULATION RULE: Always include petrol surcharge in calculation and floor decimals, but do NOT itemize petrol surcharge separately to the customer!");
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
