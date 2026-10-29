# kelimebull.com

İngilizce–Türkçe sözlük sitesi. Umbraco CMS üzerinde çalışır; madde başı (headword) araması, günün kelimesi ve OpenAI TTS ile üretilen sesli telaffuz içerir.

[![.NET](https://img.shields.io/badge/.NET-10.0-purple?logo=dotnet)](https://dotnet.microsoft.com/)
[![Umbraco](https://img.shields.io/badge/Umbraco-17.3.4-blue?logo=umbraco)](https://umbraco.com/)

## Proje yapısı

| Klasör | İçerik |
| --- | --- |
| `ClockworkUmbraco17/` | Umbraco web uygulaması (`Clockwork.Umbraco.Client.csproj`, çözüm dosyası `Clockwork.Umbraco.Client.sln`) |
| `Kelimebull.Tts.Core/` | TTS çekirdek kütüphanesi: ses kaydı tablosu erişimi, metin normalizasyonu/hash, ses seçimi, CMU telaffuz sözlüğü, SQL şeması |
| `FRONTEND/public/` | Statik HTML çalışmaları için derlenmiş Tailwind çıktısı |

`ClockworkUmbraco17/` altında öne çıkanlar:

- `Controllers/Api/` — `SearchController`, `AudioController`, `FormController`
- `Services/` — arama (Examine), günün kelimesi, `Services/Tts/` altında TTS kuyruğu, worker'lar, OpenAI istemcisi ve Cloudflare R2 depolama
- `Views/` — Razor şablonları (`Headword`, `Headwords`, `MainPage` vb.)
- `umbraco/models/` — ModelsBuilder'ın ürettiği modeller (`SourceCodeAuto` modunda; elle düzenlemeyin)
- `wwwroot/Content/tailwindcss/` — Tailwind + DaisyUI kaynakları

## Gereksinimler

- .NET 10 SDK
- SQL Server (Umbraco veritabanı ve TTS tabloları aynı veritabanında)
- Node.js / npm (yalnızca CSS derlemek için)

## Kurulum

1. Bağlantı dizesini ve gizli değerleri **user-secrets** ya da ortam değişkenleriyle verin; `appsettings.json` içine şifre/anahtar yazmayın:

   ```bash
   cd ClockworkUmbraco17
   dotnet user-secrets init
   dotnet user-secrets set "ConnectionStrings:umbracoDbDSN" "<bağlantı dizesi>"
   dotnet user-secrets set "Tts:OpenAiApiKey" "<anahtar>"
   dotnet user-secrets set "Tts:R2:AccountId" "<değer>"
   dotnet user-secrets set "Tts:R2:AccessKeyId" "<değer>"
   dotnet user-secrets set "Tts:R2:SecretAccessKey" "<değer>"
   dotnet user-secrets set "Tts:AdminKey" "<değer>"
   ```

   Ortam değişkeni olarak: `ConnectionStrings__umbracoDbDSN`, `Tts__OpenAiApiKey`, `Tts__R2__SecretAccessKey` vb.

2. TTS tablolarını oluşturun (otomatik çalışmaz, bir kez elle çalıştırılır; script idempotenttir):

   ```bash
   sqlcmd -S <sunucu> -d <veritabanı> -i Kelimebull.Tts.Core/Sql/001_create_tts_tables.sql
   ```

   Oluşan tablolar: `dbo.tts_audio_registry`, `dbo.tts_generation_queue`.

3. Uygulamayı çalıştırın:

   ```bash
   cd ClockworkUmbraco17
   dotnet run
   ```

   Varsayılan adresler: `https://localhost:44315`, `http://localhost:11097`. Backoffice: `/umbraco`.

İçerik tipleri ve ayarlar uSync ile senkronize edilir (`ClockworkUmbraco17/uSync/` git'e dahil değildir).

## CSS derleme

```bash
cd ClockworkUmbraco17/wwwroot/Content/tailwindcss
npm install
npm run css:watch   # geliştirme
npm run css:build   # minify edilmiş çıktı
```

Çıktı hem `wwwroot/Content/tailwindcss/css/output.css` hem de `FRONTEND/public/Content/tailwindcss/css/output.css` dosyasına yazılır.

## API

### Arama — `/api/dictionary`

| Metot | Yol | Parametreler |
| --- | --- | --- |
| GET | `/api/dictionary/search` | `q`, `direction` (varsayılan `en-tr`) |
| GET | `/api/dictionary/similar` | `q`, `direction`, `take` (1–50, varsayılan 10) |

### Ses — `/api/dictionary/audio`

| Metot | Yol | Açıklama |
| --- | --- | --- |
| GET | `/api/dictionary/audio?text=&language=en-US&sourceType=word` | Ses hazırsa `ready` + akış adresi, değilse kuyruğa alır ve `202 pending` döner |
| GET | `/api/dictionary/audio/status/{hash}` | Üretim durumunu sorgular |
| GET | `/api/dictionary/audio/stream/{hash}` | Ses dosyasını akıtır |
| GET | `/api/dictionary/audio/metrics` | Günlük kullanım özeti (yönetici) |
| POST | `/api/dictionary/audio/replay-failed?maxItems=100` | Başarısız kayıtları yeniden kuyruğa alır (yönetici) |
| POST | `/api/dictionary/audio/reset-stuck?includeFailed=false` | Takılı kalan kayıtları sıfırlar (yönetici) |

Yönetici uçları `X-TTS-Admin-Key` başlığını `TTS_ADMIN_KEY` (ya da `Tts:AdminKey`) değeriyle karşılaştırır; anahtar tanımlı değilse bu uçlar her zaman `401` döner. Ses uçları IP başına dakikada `Tts:ApiRequestsPerMinutePerIp` istekle sınırlıdır (aşılırsa `429`).

### Form

- POST `/umbraco/api/form/SaveContactForm`

## TTS akışı

1. İstemci `GET /api/dictionary/audio` çağırır; metin normalize edilir ve dil/ses/model/format/pipeline sürümüyle birlikte hash'lenir.
2. Kayıt `tts_audio_registry` tablosunda yoksa oluşturulur ve `tts_generation_queue` kuyruğuna eklenir.
3. `TtsGenerationWorker` kuyruğu işler, OpenAI TTS'ten sesi üretir ve Cloudflare R2'ye yükler.
4. `TtsQueueMaintenanceHostedService` takılı kayıtları düzenli olarak toparlar ve başarısız olanları belirli aralıklarla yeniden dener; `TtsWarmupHostedService` günün kelimesi seslerini önceden hazırlar.

Önemli `Tts` ayarları (`appsettings.json`):

| Ayar | Varsayılan | Açıklama |
| --- | --- | --- |
| `PipelineVersion` | `v6` | Değiştirildiğinde tüm hash'ler değişir, sesler yeniden üretilir |
| `DefaultModel` / `DefaultFormat` | `tts-1-hd` / `mp3` | |
| `Languages` | `en-US` (nova), `tr-TR` (alloy) | Dil başına model ve ses |
| `MaxTextLength` | 500 | |
| `DailyCharacterLimit` / `MonthlyCharacterLimit` | 20.000 / 400.000 | OpenAI bütçe sınırları |
| `RequestsPerMinute` | 20 | OpenAI'ye giden istek sınırı |
| `WorkerBatchSize` / `WorkerParallelism` | 10 / 1 | |
| `MaxRetryAttempts` | 3 | |
| `R2.BucketName`, `R2.PublicBaseUrl`, `CdnBaseUrl` | | Depolama ve yayın adresleri |

## Notlar

- Uygulama tüm yanıtlara `X-Robots-Tag: noindex, nofollow` ekler (`Program.cs`). Canlıya alırken kaldırılması gerekir.
- Load balancer arkasında çalışacaksa `Program.cs` içindeki `ForwardedHeaders` bloğu açılmalıdır.
