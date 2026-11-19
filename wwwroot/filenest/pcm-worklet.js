class FileNestPcmCapture extends AudioWorkletProcessor {
  constructor() {
    super();
    this.samples = [];
    this.chunkSize = Math.max(1, Math.round(sampleRate * 0.08));
  }

  process(inputs) {
    const channel = inputs[0]?.[0];
    if (!channel) return true;
    for (let index = 0; index < channel.length; index++) this.samples.push(channel[index]);
    while (this.samples.length >= this.chunkSize) {
      const pcm = new Int16Array(this.chunkSize);
      for (let index = 0; index < pcm.length; index++) {
        const sample = Math.max(-1, Math.min(1, this.samples[index]));
        pcm[index] = sample < 0 ? sample * 0x8000 : sample * 0x7fff;
      }
      this.samples.splice(0, this.chunkSize);
      this.port.postMessage(pcm.buffer, [pcm.buffer]);
    }
    return true;
  }
}

registerProcessor("filenest-pcm-capture", FileNestPcmCapture);
