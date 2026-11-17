// Тема
window.getTheme = function () {
    try { return localStorage.getItem("timetool-theme") || "dark"; } catch (e) { return "dark"; }
};
window.setTheme = function (t) {
    document.documentElement.classList.toggle("dark", t === "dark");
    try { localStorage.setItem("timetool-theme", t); } catch (e) { }
};

// Звук таймера: N коротких сигналов через WebAudio (без аудиофайлов)
window.beep = function (times) {
    try {
        var ctx = window.__ttAudio || (window.__ttAudio = new (window.AudioContext || window.webkitAudioContext)());
        if (ctx.state === "suspended") ctx.resume();
        var count = times || 3;
        for (var i = 0; i < count; i++) {
            var osc = ctx.createOscillator();
            var gain = ctx.createGain();
            osc.type = "sine";
            osc.frequency.value = 880;
            var t0 = ctx.currentTime + i * 0.35;
            gain.gain.setValueAtTime(0.0001, t0);
            gain.gain.exponentialRampToValueAtTime(0.3, t0 + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.25);
            osc.connect(gain);
            gain.connect(ctx.destination);
            osc.start(t0);
            osc.stop(t0 + 0.3);
        }
    } catch (e) { }
};

// F1 / Esc на уровне окна
window.timeToolHotkeys = (function () {
    var reference = null;
    function onKeyDown(event) {
        if (!reference) return;
        if (event.key === "F1") {
            event.preventDefault();
            event.stopPropagation();
            reference.invokeMethodAsync("ShowHelpFromHotkey");
        } else if (event.key === "Escape") {
            reference.invokeMethodAsync("CloseHelpFromHotkey");
        }
    }
    return {
        init: function (dotNetReference) {
            if (reference) window.removeEventListener("keydown", onKeyDown, true);
            reference = dotNetReference;
            window.addEventListener("keydown", onKeyDown, true);
        },
        dispose: function () {
            window.removeEventListener("keydown", onKeyDown, true);
            reference = null;
        }
    };
})();
