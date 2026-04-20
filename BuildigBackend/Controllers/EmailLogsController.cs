using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BuildigBackend.Models;
using BuildigBackend.Models.DTO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BuildigBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmailLogsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailLogsController> _logger;

        public EmailLogsController(
            ApplicationDbContext context,
            IConfiguration configuration,
            ILogger<EmailLogsController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] string? recipient,
            [FromQuery] string? status,
            [FromQuery] int take = 100,
            [FromQuery] int skip = 0)
        {
            take = Math.Clamp(take, 1, 500);
            skip = Math.Max(skip, 0);

            var query = _context.EmailLogs.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(recipient))
            {
                query = query.Where(x => x.ToEmail.Contains(recipient));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.LastEventType == status);
            }

            var logs = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(x => new
                {
                    id = x.Id,
                    createdAt = x.CreatedAt,
                    toEmail = x.ToEmail,
                    fromEmail = x.FromEmail,
                    subject = x.Subject,
                    templateId = x.TemplateId,
                    templateName = x.TemplateName,
                    lastEventType = x.LastEventType,
                    lastEventAt = x.LastEventAt,
                })
                .ToListAsync();

            return Ok(logs);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var log = await _context.EmailLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (log == null) return NotFound();

            var events = await _context.EmailLogEvents
                .AsNoTracking()
                .Where(e => e.EmailLogId == id)
                .OrderBy(e => e.Timestamp)
                .Select(e => new
                {
                    type = e.Type,
                    timestamp = e.Timestamp,
                    reason = e.Reason,
                    sgEventId = e.SgEventId,
                    response = e.Response,
                    ip = e.Ip,
                    userAgent = e.UserAgent,
                    url = e.Url,
                })
                .ToListAsync();

            return Ok(new
            {
                id = log.Id,
                createdAt = log.CreatedAt,
                fromEmail = log.FromEmail,
                toEmail = log.ToEmail,
                subject = log.Subject,
                templateId = log.TemplateId,
                templateName = log.TemplateName,
                status = log.LastEventType,
                events,
            });
        }

        [AllowAnonymous]
        [HttpPost("sendgrid/events")]
        public async Task<IActionResult> ReceiveSendGridEvents()
        {
            var payloadBytes = await ReadRawBodyBytesAsync();

            if (!VerifySendGridSignatureIfEnabled(payloadBytes))
            {
                return Unauthorized();
            }

            List<SendGridEventDto>? events;
            try
            {
                events = JsonSerializer.Deserialize<List<SendGridEventDto>>(
                    payloadBytes,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize SendGrid event webhook payload");
                return BadRequest("Invalid payload");
            }

            if (events == null || events.Count == 0)
            {
                return Ok();
            }

            int ingested = 0;

            foreach (var ev in events)
            {
                var emailLogId = Guid.TryParse(ev.EmailLogId, out var g) ? g : (Guid?)null;
                if (emailLogId == null)
                {
                    _logger.LogDebug("SendGrid event has no emailLogId, skipping. sg_event_id={SgEventId}", ev.SgEventId);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(ev.SgEventId))
                {
                    _logger.LogWarning("SendGrid event missing sg_event_id for emailLogId={EmailLogId}", emailLogId);
                    continue;
                }

                var exists = await _context.EmailLogEvents
                    .AsNoTracking()
                    .AnyAsync(x => x.SgEventId == ev.SgEventId);

                if (exists)
                {
                    continue;
                }

                var log = await _context.EmailLogs.FirstOrDefaultAsync(x => x.Id == emailLogId.Value);
                if (log == null)
                {
                    _logger.LogWarning(
                        "Received SendGrid event for unknown emailLogId={EmailLogId}",
                        emailLogId);
                    continue;
                }

                var ts = DateTimeOffset.FromUnixTimeSeconds(ev.Timestamp).UtcDateTime;
                var type = ev.Event ?? "unknown";

                var entity = new EmailLogEvent
                {
                    EmailLogId = log.Id,
                    Email = ev.Email ?? log.ToEmail,
                    Type = type,
                    Timestamp = ts,
                    SgEventId = ev.SgEventId,
                    SmtpId = ev.SmtpId,
                    Reason = ev.Reason,
                    Response = ev.Response,
                    Ip = ev.Ip,
                    UserAgent = ev.UserAgent,
                    Url = ev.Url,
                };

                _context.EmailLogEvents.Add(entity);

                log.LastEventType = type;
                log.LastEventAt = ts;

                ingested++;
            }

            if (ingested > 0)
            {
                await _context.SaveChangesAsync();
            }

            return Ok(new { ingested });
        }

        private async Task<byte[]> ReadRawBodyBytesAsync()
        {
            Request.EnableBuffering();
            Request.Body.Position = 0;

            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms);
            Request.Body.Position = 0;
            return ms.ToArray();
        }

        private bool VerifySendGridSignatureIfEnabled(byte[] payloadBytes)
        {
            var verificationKey = _configuration["SendGrid:EventWebhookToken"];
            if (string.IsNullOrWhiteSpace(verificationKey))
            {
                return true;
            }

            var signatureB64 = Request.Headers["X-Twilio-Email-Event-Webhook-Signature"].ToString();
            var timestamp = Request.Headers["X-Twilio-Email-Event-Webhook-Timestamp"].ToString();

            if (string.IsNullOrWhiteSpace(signatureB64) || string.IsNullOrWhiteSpace(timestamp))
            {
                _logger.LogWarning("Missing SendGrid signed webhook headers");
                return false;
            }

            // Optional replay protection window (5 minutes)
            if (long.TryParse(timestamp, out var ts))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (Math.Abs(now - ts) > 300)
                {
                    _logger.LogWarning("SendGrid webhook timestamp outside allowed window");
                    return false;
                }
            }

            byte[] publicKey;
            byte[] signatureBytes;
            try
            {
                // This is the base64 SPKI public key shown in the SendGrid UI.
                publicKey = Convert.FromBase64String(verificationKey);
                // This is the base64-encoded ECDSA signature header.
                signatureBytes = Convert.FromBase64String(signatureB64);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid base64 in SendGrid verification key or signature");
                return false;
            }

            // Per SendGrid docs: hash = sha256(timestamp_bytes + payload_bytes)
            var tsBytes = Encoding.UTF8.GetBytes(timestamp);
            var combined = new byte[tsBytes.Length + payloadBytes.Length];
            Buffer.BlockCopy(tsBytes, 0, combined, 0, tsBytes.Length);
            Buffer.BlockCopy(payloadBytes, 0, combined, tsBytes.Length, payloadBytes.Length);
            var hash = SHA256.HashData(combined);

            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);

                // Convert DER-encoded signature to IEEE P1363 format that .NET expects
                var ieeeSignature = ConvertDerToIeeeP1363(signatureBytes);

                var ok = ecdsa.VerifyHash(hash, ieeeSignature);
                if (!ok)
                {
                    _logger.LogWarning("SendGrid signed webhook verification failed");
                }
                return ok;
            }
            catch (CryptographicException ex)
            {
                _logger.LogWarning(ex, "Failed to import/verify SendGrid webhook public key");
                return false;
            }
        }
        private static byte[] ConvertDerToIeeeP1363(byte[] derSignature)
        {
            // DER structure: 0x30 [total-len] 0x02 [r-len] [r-bytes] 0x02 [s-len] [s-bytes]
            if (derSignature[0] != 0x30)
                throw new CryptographicException("Not a DER sequence");

            int offset = 2; // skip 0x30 and total length byte

            // Parse R
            if (derSignature[offset] != 0x02)
                throw new CryptographicException("Expected INTEGER tag for R");
            offset++;
            int rLen = derSignature[offset++];
            byte[] r = derSignature.Skip(offset).Take(rLen).ToArray();
            offset += rLen;

            // Parse S
            if (derSignature[offset] != 0x02)
                throw new CryptographicException("Expected INTEGER tag for S");
            offset++;
            int sLen = derSignature[offset++];
            byte[] s = derSignature.Skip(offset).Take(sLen).ToArray();

            // Strip leading zero padding (DER uses it to indicate positive integer)
            // then pad to 32 bytes each for P-256
            const int coordSize = 32;
            var result = new byte[coordSize * 2];

            // Copy R right-aligned into first 32 bytes
            var rTrimmed = r.SkipWhile(b => b == 0).ToArray();
            Buffer.BlockCopy(rTrimmed, 0, result, coordSize - rTrimmed.Length, rTrimmed.Length);

            // Copy S right-aligned into last 32 bytes  
            var sTrimmed = s.SkipWhile(b => b == 0).ToArray();
            Buffer.BlockCopy(sTrimmed, 0, result, coordSize * 2 - sTrimmed.Length, sTrimmed.Length);

            return result;
        }
        private static Guid? TryGetEmailLogId(Dictionary<string, object>? customArgs)
        {
            if (customArgs == null) return null;

            if (customArgs.TryGetValue("emailLogId", out var raw) && raw != null)
            {
                if (raw is string s && Guid.TryParse(s, out var g1)) return g1;
                if (Guid.TryParse(raw.ToString(), out var g2)) return g2;
            }

            return null;
        }
    }
}

