using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VisualMusicBlockLooper.Recognition
{
    internal sealed class ImageClassifier : IDisposable
    {
        private const string Origin = "https://looper.local";
        private const string Home = Origin + "/index.html";

        private readonly Control owner;
        private readonly string profile;
        private readonly Dictionary<string, byte[]> assets = new Dictionary<string, byte[]>();
        private WebView2 browser;
        private ModelPackage candidate;
        private TaskCompletionSource<JObject> response;
        private string requestId;

        public ImageClassifier(Control owner, string profile)
        {
            this.owner = owner;
            this.profile = profile;
        }

        public ModelPackage Active { get; private set; }

        public async Task LoadAsync(ModelPackage package)
        {
            if (Active?.Id == package.Id)
                return;

            await InitializeAsync();
            candidate = package;
            try
            {
                var ready = await SendAsync(new JObject { ["op"] = "load", ["model"] = package.Id }, 30);
                if (!(ready["labels"] is JArray labels) || !labels.Values<string>().SequenceEqual(package.Labels))
                    throw new InvalidDataException("Le etichette non corrispondono all'uscita del modello.");

                if (Active != null)
                    Release(Active.Id);
                Active = package;
            }
            catch
            {
                Release(package.Id);
                throw;
            }
            finally
            {
                candidate = null;
            }
        }

        public async Task<double[][]> PredictAsync(byte[][] cells)
        {
            if (Active == null)
                throw new InvalidOperationException("Importa un modello prima di riconoscere i simboli.");

            var payload = new JArray();
            foreach (var png in cells)
                payload.Add(png == null ? JValue.CreateNull() : new JValue(Convert.ToBase64String(png)));

            var reply = await SendAsync(new JObject
            {
                ["op"] = "predict", ["model"] = Active.Id, ["images"] = payload
            }, 60);

            if (!(reply["scores"] is JArray scores) || scores.Count != cells.Length)
                throw new InvalidDataException("Numero di predizioni inatteso.");

            var result = new double[cells.Length][];
            for (int index = 0; index < cells.Length; index++)
            {
                if (cells[index] == null && scores[index].Type == JTokenType.Null)
                    continue;

                if (!(scores[index] is JArray scoresForCell) || scoresForCell.Count != Active.Labels.Length ||
                    scoresForCell.Any(score => score.Type != JTokenType.Float && score.Type != JTokenType.Integer))
                    throw new InvalidDataException("Il modello ha restituito una predizione non valida.");

                var values = scoresForCell.Values<double>().ToArray();
                if (values.Any(value => double.IsNaN(value) || value < 0 || value > 1.00001))
                    throw new InvalidDataException("Il modello ha restituito una confidenza non valida.");
                result[index] = values;
            }
            return result;
        }

        private async Task InitializeAsync()
        {
            if (browser != null)
                return;

            try
            {
                var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Inference");
                foreach (var asset in new[] { "index.html", "classifier.js", "Vendor/tf.min.js", "Vendor/teachablemachine-image.min.js" })
                    assets["/" + asset] = File.ReadAllBytes(Path.Combine(folder, asset));

                browser = new WebView2 { Width = 1, Height = 1, TabStop = false };
                owner.Controls.Add(browser);
                browser.SendToBack();
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
                await browser.EnsureCoreWebView2Async(environment);

                var core = browser.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.NavigationStarting += (sender, args) => args.Cancel = args.Uri != Home;
                core.NewWindowRequested += (sender, args) => args.Handled = true;
                core.WebMessageReceived += OnMessage;
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
                    CoreWebView2WebResourceRequestSourceKinds.All);
                core.WebResourceRequested += Serve;

                var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler<CoreWebView2NavigationCompletedEventArgs> completed =
                    (sender, args) => navigation.TrySetResult(args.IsSuccess);
                core.NavigationCompleted += completed;
                try
                {
                    core.Navigate(Home);
                    if (!await WithTimeout(navigation.Task, 15))
                        throw new IOException("Avvio della pagina di riconoscimento locale non riuscito.");
                }
                finally
                {
                    core.NavigationCompleted -= completed;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Serve(object sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            byte[] bytes = null;
            var uri = new Uri(args.Request.Uri);
            if (uri.Scheme == "https" && uri.Host == "looper.local" && uri.IsDefaultPort && args.Request.Method == "GET")
            {
                if (!assets.TryGetValue(uri.AbsolutePath, out bytes))
                {
                    var parts = uri.AbsolutePath.Split(new[] { '/' }, 4);
                    if (parts.Length == 4 && parts[1] == "models")
                    {
                        var model = candidate?.Id == parts[2] ? candidate : Active;
                        if (model?.Id == parts[2])
                            model.Files.TryGetValue(parts[3], out bytes);
                    }
                }
            }

            var environment = browser.CoreWebView2.Environment;
            if (bytes == null)
            {
                args.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }

            string extension = Path.GetExtension(uri.AbsolutePath);
            string mime = extension == ".html" ? "text/html" : extension == ".js" ? "text/javascript" :
                extension == ".json" ? "application/json" : "application/octet-stream";
            args.Response = environment.CreateWebResourceResponse(new MemoryStream(bytes, false), 200, "OK",
                "Content-Type: " + mime + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n" +
                "Content-Security-Policy: default-src 'none'; script-src 'self'; connect-src 'self'; img-src data:");
        }

        private async Task<JObject> SendAsync(JObject request, int seconds)
        {
            if (response != null)
                throw new InvalidOperationException("È già in corso una richiesta al classificatore.");

            response = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            requestId = Guid.NewGuid().ToString("N");
            request["id"] = requestId;
            try
            {
                browser.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(request, Formatting.None));
                var reply = await WithTimeout(response.Task, seconds);
                if (reply["error"] != null)
                    throw new InvalidDataException((string)reply["error"]);
                return reply;
            }
            finally
            {
                response = null;
            }
        }

        private void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (args.Source != Home || response == null)
                return;

            try
            {
                var message = JObject.Parse(args.WebMessageAsJson);
                if ((string)message["id"] == requestId)
                    response.TrySetResult(message);
            }
            catch (Newtonsoft.Json.JsonException error)
            {
                response.TrySetException(error);
            }
        }

        private void Release(string id)
        {
            browser.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(new JObject
            {
                ["op"] = "release", ["model"] = id
            }, Formatting.None));
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, int seconds)
        {
            using (var cancelDelay = new System.Threading.CancellationTokenSource())
            {
                if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds), cancelDelay.Token)) != task)
                    throw new TimeoutException("Il motore di riconoscimento non risponde.");
                cancelDelay.Cancel();
                return await task;
            }
        }

        public void Dispose()
        {
            response?.TrySetCanceled();
            browser?.Dispose();
            browser = null;
            Active = null;
        }
    }
}
