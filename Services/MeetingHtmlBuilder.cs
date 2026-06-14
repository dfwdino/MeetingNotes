using System.IO;
using System.Net;
using System.Text;

namespace MeetingNotes.Services;

public static class MeetingHtmlBuilder
{
    // ── Plain (unencrypted) HTML ──────────────────────────────────────────

    public static string BuildPlain(
        string title, DateTime date, string durationDisplay,
        string? audioFileName, string? notes, string? summary, string? transcript)
    {
        var meta = BuildMeta(date, durationDisplay);
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"  <title>{HtmlEncode(title)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;max-width:820px;margin:48px auto;padding:0 24px;color:#1a1a1a;background:#fff;line-height:1.7}");
        sb.AppendLine("    h1{font-size:26px;font-weight:700;margin:0 0 6px;color:#111}");
        sb.AppendLine("    .meta{color:#666;font-size:14px;margin-bottom:24px}");
        sb.AppendLine("    audio{width:100%;margin-bottom:28px;border-radius:4px}");
        sb.AppendLine("    hr{border:none;border-top:1px solid #e8e8e8;margin:28px 0}");
        sb.AppendLine("    h2{font-size:18px;font-weight:700;color:#0078d4;margin:0 0 10px}");
        sb.AppendLine("    .txt{font-size:15px;white-space:pre-wrap;word-break:break-word;color:#1a1a1a;margin-bottom:32px}");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine($"  <h1>{HtmlEncode(title)}</h1>");
        sb.AppendLine($"  <div class=\"meta\">{meta}</div>");

        if (audioFileName is not null)
        {
            var mime = Path.GetExtension(audioFileName).ToLowerInvariant() == ".wav" ? "audio/wav" : "audio/mpeg";
            sb.AppendLine("  <audio controls>");
            sb.AppendLine($"    <source src=\"{HtmlEncode(audioFileName)}\" type=\"{mime}\">");
            sb.AppendLine("  </audio>");
        }

        sb.AppendLine("  <hr>");
        AppendSection(sb, "My Notes", notes);
        AppendSection(sb, "AI Summary", summary);
        AppendSection(sb, "Transcript", transcript);
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    // ── Self-decrypting (password-protected) HTML ─────────────────────────
    // Ciphertext is stored verbatim from the DB (nonce[12]+ciphertext+tag[16], base64).
    // The browser Web Crypto API replicates the same PBKDF2-SHA256 + AES-256-GCM scheme.
    // Notes may decrypt to raw RTF if the user used formatting — the JS detects this and
    // shows a friendly message rather than rendering markup soup.

    public static string BuildEncrypted(
        string title, DateTime date, string durationDisplay,
        string encryptionSalt, string encryptedDataKey,
        string? encNotes, string? encSummary, string? encTranscript)
    {
        var titleHtml = HtmlEncode(title);
        var meta      = BuildMeta(date, durationDisplay);
        var jsN  = JsStringOrNull(encNotes);
        var jsSm = JsStringOrNull(encSummary);
        var jsT  = JsStringOrNull(encTranscript);

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
              <title>🔒 {{titleHtml}}</title>
              <style>
                *{box-sizing:border-box;margin:0;padding:0}
                body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#111;min-height:100vh}
                #lock{display:flex;align-items:center;justify-content:center;min-height:100vh;padding:24px}
                .card{background:#1a1a2e;border:1px solid #3a3a5a;border-radius:12px;padding:48px 40px;text-align:center;max-width:420px;width:100%}
                .icon{font-size:48px;margin-bottom:20px}
                .ltitle{font-size:22px;font-weight:700;color:#e8e8ff;margin-bottom:6px}
                .lmeta{color:#8888aa;font-size:13px;margin-bottom:20px}
                .hint{color:#6666aa;font-size:13px;margin-bottom:28px;line-height:1.5}
                #pwd{width:100%;padding:10px 14px;background:#0d0d1a;border:1px solid #3a3a5a;border-radius:6px;color:#e8e8ff;font-size:15px;outline:none;margin-bottom:12px}
                #pwd:focus{border-color:#0078d4}
                #ubtn{width:100%;padding:11px;background:#0078d4;color:#fff;border:none;border-radius:6px;font-size:15px;font-weight:600;cursor:pointer}
                #ubtn:hover{background:#006bb8}
                #ubtn:disabled{opacity:.6;cursor:default}
                #err{color:#ef5350;font-size:13px;margin-top:12px;min-height:20px}
                body.shown{background:#fff}
                #content{display:none;max-width:820px;margin:48px auto;padding:0 24px}
                h1{font-size:26px;font-weight:700;margin:0 0 6px;color:#111}
                .meta{color:#666;font-size:14px;margin-bottom:24px}
                hr{border:none;border-top:1px solid #e8e8e8;margin:28px 0}
                h2{font-size:18px;font-weight:700;color:#0078d4;margin:0 0 10px}
                .txt{font-size:15px;white-space:pre-wrap;word-break:break-word;color:#1a1a1a;margin-bottom:32px}
                .rtf-note{font-size:13px;color:#888;font-style:italic}
              </style>
            </head>
            <body>
              <div id="lock">
                <div class="card">
                  <div class="icon">🔒</div>
                  <div class="ltitle">{{titleHtml}}</div>
                  <div class="lmeta">{{meta}}</div>
                  <div class="hint">This meeting is password-protected.<br>Enter your password to view the content.</div>
                  <form id="form">
                    <input type="password" id="pwd" placeholder="Password" autocomplete="current-password" autofocus>
                    <button type="submit" id="ubtn">Unlock</button>
                    <p id="err"></p>
                  </form>
                </div>
              </div>
              <div id="content">
                <h1>{{titleHtml}}</h1>
                <div class="meta">{{meta}}</div>
                <hr>
                <div id="ns" style="display:none">
                  <h2>My Notes</h2>
                  <div class="txt" id="nt"></div>
                </div>
                <div id="ss" style="display:none">
                  <h2>AI Summary</h2>
                  <div class="txt" id="st"></div>
                </div>
                <div id="ts" style="display:none">
                  <h2>Transcript</h2>
                  <div class="txt" id="tt"></div>
                </div>
              </div>
              <script>
                const S="{{encryptionSalt}}",W="{{encryptedDataKey}}",N={{jsN}},SM={{jsSm}},T={{jsT}};
                const b=s=>Uint8Array.from(atob(s),c=>c.charCodeAt(0));
                async function df(k,s){
                  if(!s)return null;
                  const d=b(s);
                  const p=await crypto.subtle.decrypt({name:'AES-GCM',iv:d.slice(0,12)},k,d.slice(12));
                  return new TextDecoder().decode(p);
                }
                function showField(secId,txtId,val,checkRtf){
                  if(!val)return;
                  document.getElementById(secId).style.display='block';
                  const el=document.getElementById(txtId);
                  if(checkRtf&&val.trimStart().startsWith('{\\rtf')){
                    el.innerHTML='<span class="rtf-note">Notes contain rich formatting — open in the Meeting Notes app to view them correctly.</span>';
                  }else{
                    el.textContent=val;
                  }
                }
                document.getElementById('form').addEventListener('submit',async e=>{
                  e.preventDefault();
                  const btn=document.getElementById('ubtn');
                  btn.disabled=true;
                  document.getElementById('err').textContent='';
                  try{
                    const km=await crypto.subtle.importKey('raw',new TextEncoder().encode(document.getElementById('pwd').value),'PBKDF2',false,['deriveKey']);
                    const kek=await crypto.subtle.deriveKey({name:'PBKDF2',salt:b(S),iterations:200000,hash:'SHA-256'},km,{name:'AES-GCM',length:256},false,['decrypt']);
                    const wk=b(W);
                    const dkb=await crypto.subtle.decrypt({name:'AES-GCM',iv:wk.slice(0,12)},kek,wk.slice(12));
                    const dk=await crypto.subtle.importKey('raw',dkb,{name:'AES-GCM'},false,['decrypt']);
                    const[notes,sum,tran]=[await df(dk,N),await df(dk,SM),await df(dk,T)];
                    document.getElementById('lock').style.display='none';
                    document.getElementById('content').style.display='block';
                    document.body.classList.add('shown');
                    showField('ns','nt',notes,true);
                    showField('ss','st',sum,false);
                    showField('ts','tt',tran,false);
                  }catch{
                    document.getElementById('err').textContent='Incorrect password. Please try again.';
                    btn.disabled=false;
                  }
                });
              </script>
            </body>
            </html>
            """;
    }

    // ── Shared helpers ────────────────────────────────────────────────────

    public static string SanitizeName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }

    public static string ExtractPlainTextFromRtf(string? content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        if (!content.TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal)) return content;
        try
        {
            var doc = new System.Windows.Documents.FlowDocument();
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
            new System.Windows.Documents.TextRange(doc.ContentStart, doc.ContentEnd)
                .Load(ms, System.Windows.DataFormats.Rtf);
            return new System.Windows.Documents.TextRange(doc.ContentStart, doc.ContentEnd).Text.Trim();
        }
        catch
        {
            return content;
        }
    }

    private static void AppendSection(StringBuilder sb, string label, string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        sb.AppendLine($"  <h2>{label}</h2>");
        sb.AppendLine($"  <div class=\"txt\">{HtmlEncode(content)}</div>");
    }

    private static string BuildMeta(DateTime date, string durationDisplay)
    {
        var duration = string.IsNullOrWhiteSpace(durationDisplay)
            ? string.Empty
            : $" &nbsp;·&nbsp; {HtmlEncode(durationDisplay)}";
        return $"{date:MMMM d, yyyy}{duration}";
    }

    private static string JsStringOrNull(string? value) =>
        value is null ? "null" : $"\"{value}\"";

    private static string HtmlEncode(string? text) =>
        WebUtility.HtmlEncode(text ?? string.Empty);
}
