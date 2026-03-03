using System.Text.Json;
using AutoClickMaui.Services;

namespace AutoClickMaui;

public partial class MainPage : ContentPage
{
	private readonly AutoClickEngine _engine;
	private readonly ProfileStore _profileStore;
	private static readonly JsonSerializerOptions JsOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	public MainPage(AutoClickEngine engine, ProfileStore profileStore)
	{
		InitializeComponent();
		_engine = engine;
		_profileStore = profileStore;

		AppWebView.Navigating += OnWebViewNavigating;
		LoadReactUi();
	}

	private async void LoadReactUi()
	{
		try
		{
			using var stream = await FileSystem.OpenAppPackageFileAsync("wwwroot/react-ui.html");
			using var reader = new StreamReader(stream);
			var html = await reader.ReadToEndAsync();
			AppWebView.Source = new HtmlWebViewSource { Html = html };
		}
		catch (Exception ex)
		{
			var fallback = $"<html><body style='font-family:Segoe UI;padding:16px;background:#111;color:#eee;'><h3>Error cargando UI</h3><pre>{System.Net.WebUtility.HtmlEncode(ex.ToString())}</pre></body></html>";
			AppWebView.Source = new HtmlWebViewSource { Html = fallback };
		}
	}

	private async void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
	{
		if (!e.Url.StartsWith("app://invoke", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		e.Cancel = true;
		var requestId = string.Empty;

		try
		{
			var uri = new Uri(e.Url);
			var query = uri.Query.TrimStart('?');
			var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
			var payloadPart = parts.FirstOrDefault(p => p.StartsWith("payload=", StringComparison.OrdinalIgnoreCase));
			if (payloadPart is null)
			{
				return;
			}

			var payloadEncoded = payloadPart.Substring("payload=".Length);
			var payloadJson = Uri.UnescapeDataString(payloadEncoded);
			using var doc = JsonDocument.Parse(payloadJson);
			var root = doc.RootElement;
			requestId = root.GetProperty("requestId").GetString() ?? string.Empty;
			var type = root.GetProperty("type").GetString() ?? string.Empty;

			var response = await HandleCommandAsync(type, root);
			await SendToJsAsync(new
			{
				kind = "response",
				requestId,
				success = true,
				data = response
			});
		}
		catch (Exception ex)
		{
			await SendToJsAsync(new
			{
				kind = "response",
				requestId,
				success = false,
				error = ex.Message
			});
		}
	}

	private async Task<object> HandleCommandAsync(string type, JsonElement root)
	{
		switch (type)
		{
			case "RunSelfTest":
			{
				var checks = new List<object>();
				var allOk = true;

				checks.Add(new
				{
					name = "Firma de proyecto",
					ok = true,
					detail = $"{ProjectIdentity.Signature} · {ProjectIdentity.OwnerEmail}"
				});

				var monitors = _engine.GetMonitors();
				var monitorsOk = monitors.Count > 0;
				checks.Add(new { name = "Monitores detectados", ok = monitorsOk, detail = $"Detectados: {monitors.Count}" });
				allOk &= monitorsOk;

				var captureOk = false;
				var captureDetail = "Sin monitores para capturar.";
				if (monitorsOk)
				{
					try
					{
						var screenshot = _engine.CaptureMonitorPngBase64(monitors[0].Id);
						captureOk = !string.IsNullOrWhiteSpace(screenshot) && screenshot.Length > 100;
						captureDetail = captureOk ? "Captura OK." : "Captura vacía.";
					}
					catch (Exception ex)
					{
						captureDetail = $"Error captura: {ex.Message}";
					}
				}
				checks.Add(new { name = "Captura de pantalla", ok = captureOk, detail = captureDetail });
				allOk &= captureOk;

				var profileName = $"__selftest__{DateTime.UtcNow:yyyyMMddHHmmss}";
				var profileOk = false;
				var profileDetail = "";
				try
				{
					var testProfile = new AutoClickProfile
					{
						Name = profileName,
						MonitorId = monitorsOk ? monitors[0].Id : "0",
						ExecutionMode = "any",
						IntervalMs = 250,
						CooldownMs = 500,
						Actions = new List<ActionStepDto>
						{
							new()
							{
								Name = "SelfTest",
								Threshold = 0.8,
								TemplateBase64 = "",
								ClickPoint = new PointDto { X = 0, Y = 0 }
							}
						}
					};

					await _profileStore.SaveAsync(testProfile);
					var loaded = await _profileStore.LoadAsync(profileName);
					profileOk = loaded is not null && loaded.Name == profileName;
					profileDetail = profileOk ? "Guardar/cargar perfil OK." : "No se pudo leer el perfil guardado.";
					await _profileStore.DeleteAsync(profileName);
				}
				catch (Exception ex)
				{
					profileDetail = $"Error perfiles: {ex.Message}";
				}
				checks.Add(new { name = "Persistencia de perfiles", ok = profileOk, detail = profileDetail });
				allOk &= profileOk;

				var statusOk = !string.IsNullOrWhiteSpace(_engine.LastStatus);
				checks.Add(new { name = "Estado del motor", ok = statusOk, detail = _engine.LastStatus });
				allOk &= statusOk;

				return new
				{
					ok = allOk,
					timestampUtc = DateTime.UtcNow,
					checks
				};
			}

			case "GetMonitors":
				return new { monitors = _engine.GetMonitors() };

			case "CaptureMonitor":
			{
				var monitorId = root.TryGetProperty("monitorId", out var monitorProp)
					? monitorProp.GetString() ?? ""
					: "";
				var base64 = _engine.CaptureMonitorJpegBase64(monitorId, 70);
				return new { screenshotBase64 = base64 };
			}

			case "StartDetection":
			{
				var req = JsonSerializer.Deserialize<StartDetectionRequest>(root.GetRawText(), JsOptions)
				          ?? throw new InvalidOperationException("Payload inválido.");

				_engine.Start(req);
				return new { started = true, running = _engine.IsRunning, status = _engine.LastStatus };
			}

			case "StopDetection":
				_engine.Stop();
				return new { stopped = true };

			case "GetStatus":
				return new { status = _engine.LastStatus, running = _engine.IsRunning };

			case "SaveProfile":
			{
				var req = JsonSerializer.Deserialize<SaveProfileRequest>(root.GetRawText(), JsOptions)
				          ?? throw new InvalidOperationException("Payload inválido.");
				await _profileStore.SaveAsync(req.Profile);
				return new { saved = true };
			}

			case "ListProfiles":
			{
				var profiles = await _profileStore.ListAsync();
				return new { profiles };
			}

			case "LoadProfile":
			{
				var name = root.TryGetProperty("name", out var nameProp)
					? nameProp.GetString() ?? ""
					: "";
				if (string.IsNullOrWhiteSpace(name))
				{
					throw new InvalidOperationException("Debes seleccionar un perfil.");
				}
				var profile = await _profileStore.LoadAsync(name)
				             ?? throw new InvalidOperationException("Perfil no encontrado.");
				return new { profile };
			}

			default:
				throw new InvalidOperationException($"Comando no soportado: {type}");
		}
	}

	private async Task SendToJsAsync(object payload)
	{
		var json = JsonSerializer.Serialize(payload);
		await AppWebView.EvaluateJavaScriptAsync($"window.onNativeMessage({json});");
	}
}
