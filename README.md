# Visual Music Block Looper

Disegna simboli su un foglio, fotografali con la webcam e ascolta la sequenza. Le quattro righe di ogni blocco suonano in parallelo. L'app riconosce il formato dai quattro marcatori.

| Foglio da stampare | Sequenza | Durata di una casella |
| --- | --- | --- |
| [Foglio 1 · 4×8](assets/foglio-loop-a4.pdf) | 8 passi | Un quarto |
| [Foglio 2 · due blocchi 4×16](assets/foglio-loop-32-passi-a4.pdf) | Passi 1–16 sopra, poi 17–32 sotto | Un sedicesimo |

Entrambi i fogli coprono otto quarti. A 120 BPM il giro dura 4 secondi in entrambi i formati.

## Avvio su Windows

Estrai l'intero archivio di distribuzione e apri `VisualMusicBlockLooper.exe`. L'app si apre a tutto schermo. Premi **Esc** per passare alla finestra ridimensionabile.

Serve un PC Windows x64 con una versione di .NET Framework compatibile con il target **4.7**, WebView2 Runtime, Visual C++ Redistributable x64, una webcam e un'uscita audio. Per un laboratorio senza rete, prepara il programma di installazione offline di WebView2 sui PC. L'app usa i file locali per il riconoscimento e l'audio. [Versioni .NET Framework](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies) · [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) · [Visual C++ Redistributable](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)

## Dal foglio al loop

1. Scegli uno dei due fogli e stampalo in A4 orizzontale a dimensione effettiva (100%). Disegna un solo simbolo per casella, dentro il bordo. Lascia in bianco le pause.
2. Seleziona la webcam. Premi **Importa modello** e scegli lo ZIP scaricato da Teachable Machine con **Image Project → Export Model → TensorFlow.js → Download**. Puoi iniziare con il [modello incluso](assets/modelli%20teachable%20machine/tm-my-image-model.zip), che riconosce cerchio, quadrato, croce e triangolo.
3. Per ogni etichetta scegli un WAV oppure **Nessun suono**. Il campione selezionato si sente subito. Sono inclusi otto campioni acustici ed elettronici. Per usare i tuoi file scegli **Cartella WAV**. Usa campioni WAV mono o stereo.
4. Inquadra un solo foglio e tutti e quattro i suoi marcatori, in buona luce, poi premi **Acquisisci**. L'app indica il formato rilevato e sovrappone alla foto i bordi riconosciuti e i nomi dei campioni. Premi **Torna alla webcam** per rivedere l'anteprima.
5. Premi **Start**. La colonna corrente si illumina nel blocco che sta suonando. **Stop** interrompe il loop.

La soglia iniziale è 85%. Puoi regolarla e vedere subito il risultato sulla stessa foto. Per addestrare un nuovo modello raccogli esempi delle singole caselle, con disegni, illuminazione e sfondo simili a quelli d'uso. Aggiungi una classe `VUOTO` con esempi senza disegni. L'app lascia in silenzio quella classe e filtra anche le caselle chiaramente bianche.

Le scelte sono salvate in `%LocalAppData%\VisualMusicBlockLooper\scena.json`, con una copia della versione precedente in `scena.json.bak` dopo il primo aggiornamento. Le associazioni sono salvate per modello. Il percorso iniziale dei campioni è `assets/suoni` nella cartella dell'eseguibile. Le foto restano in memoria durante la sessione.

[Crediti dei WAV](assets/suoni/CREDITI.md). Gli avvisi di TensorFlow.js e Teachable Machine si trovano in `Inference/Vendor/NOTICE.md` nella distribuzione.

## Build, distribuzione e test

Apri `VisualMusicBlockLooper.sln` in Visual Studio con il carico di lavoro **Sviluppo desktop .NET**, oppure usa il .NET SDK su Windows. Il pacchetto NuGet delle reference assemblies compila per `net47` anche se sul PC è installata una versione successiva di .NET Framework.

```powershell
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Release
dotnet publish src/VisualMusicBlockLooper -c Release -o artifacts/VisualMusicBlockLooper
```

I test coprono il modello incluso in WebView2, la griglia ruotata e in prospettiva, il ripristino della scena e il mixer. I test con categoria `Hardware` usano l'uscita audio predefinita e una webcam USB, se presente. I test con categoria `WebView2` richiedono il runtime e una sessione desktop.

Quando cambia un layout, rigenera il relativo SVG dalla cartella principale del repository, poi esegui lo script PDF con Python e ReportLab.

```powershell
$app = 'src/VisualMusicBlockLooper/bin/Release/net47/VisualMusicBlockLooper.exe'
Start-Process -FilePath $app -ArgumentList '--export-grid assets/foglio-loop-a4.svg' -WindowStyle Hidden -Wait
python scripts/export-grid-pdf.py
Start-Process -FilePath $app -ArgumentList '--export-grid-32 assets/foglio-loop-32-passi-a4.svg' -WindowStyle Hidden -Wait
python scripts/export-grid-pdf.py assets/foglio-loop-32-passi-a4.svg
```
