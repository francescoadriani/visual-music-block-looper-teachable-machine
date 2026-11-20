"use strict";

const models = new Map();
let queue = Promise.resolve();

function decodePng(bytes) {
    return new Promise((resolve, reject) => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = () => reject(new Error("Ritaglio immagine non valido."));
        image.src = "data:image/png;base64," + bytes;
    });
}

async function handle(request) {
    if (request.op === "release") {
        const existing = models.get(request.model);
        if (existing) {
            models.delete(request.model);
            existing.model.dispose();
        }
        return;
    }

    const reply = { id: request.id };
    try {
        if (request.op === "load") {
            const root = "/models/" + request.model;
            const model = await tmImage.load(root + "/model.json", root + "/metadata.json");
            const labels = model.getClassLabels();
            if (model.getTotalClasses() !== labels.length) {
                model.model.dispose();
                throw new Error("Il numero delle etichette non coincide con quello delle classi.");
            }
            models.set(request.model, model);
            reply.labels = labels;
        } else if (request.op === "predict") {
            const model = models.get(request.model);
            if (!model) {
                throw new Error("Modello non caricato.");
            }
            reply.scores = [];
            for (const bytes of request.images) {
                if (bytes === null) {
                    reply.scores.push(null);
                } else {
                    const image = await decodePng(bytes);
                    const predictions = await model.predict(image);
                    reply.scores.push(predictions.map(item => item.probability));
                }
            }
        }
    } catch (error) {
        reply.error = error.message || String(error);
    }
    window.chrome.webview.postMessage(reply);
}

window.chrome.webview.addEventListener("message", event => {
    queue = queue.catch(error => console.error(error)).then(() => handle(event.data));
});
