import sys, os, pathlib
from faster_whisper import WhisperModel

def ts(t: float) -> str:
    # WebVTT timestamp (HH:MM:SS.mmm)
    if t < 0: t = 0
    h = int(t // 3600)
    m = int((t % 3600) // 60)
    s = int(t % 60)
    ms = int(round((t - int(t)) * 1000))
    return f"{h:02d}:{m:02d}:{s:02d}.{ms:03d}"

def main():
    if len(sys.argv) < 3:
        print("Usage: python make_vtt.py <input_video> <output_vtt>")
        print("Example: python make_vtt.py ./public/Assets/PeteVideo-desktop.mp4 ./public/Assets/hero.en.vtt")
        sys.exit(1)

    src = pathlib.Path(sys.argv[1])
    out = pathlib.Path(sys.argv[2])
    out.parent.mkdir(parents=True, exist_ok=True)

    # Choose a model:
    #  - "small.en": fast, decent
    #  - "medium.en": better
    #  - "large-v3": best (slowest)
    model_name = "medium.en"

    # CPU-friendly defaults; set device="cuda" if you have a GPU
    model = WhisperModel(model_name, device="cpu", compute_type="int8")

    print(f"Transcribing {src} → {out} using {model_name} ...")
    segments, info = model.transcribe(
        str(src),
        language="en",
        beam_size=5,
        vad_filter=True,          # better segmentation
        vad_parameters=dict(min_silence_duration_ms=400)
    )

    # Write WebVTT
    with open(out, "w", encoding="utf-8") as f:
        f.write("WEBVTT\n\n")
        for i, seg in enumerate(segments, start=1):
            start = ts(seg.start)
            end   = ts(seg.end)
            text  = (seg.text or "").strip()
            # Optional: tidy spaces
            text = " ".join(text.split())
            f.write(f"{start} --> {end}\n{text}\n\n")

    print("Done.")

if __name__ == "__main__":
    main()
