using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;

namespace GolosovedAI.Services
{
    public static class VadService
    {
        public static string RemoveSilence(
            string wavPath,
            double silenceThresholdDb = -40,
            int minSpeechBlocks = 10,   // 200 мс
            int minSilenceBlocks = 30,  // 600 мс
            int preSpeechBlocks = 5)    // 100 мс запас перед речью (чтобы не резало первые звуки)
        {
            using var reader = new WaveFileReader(wavPath);
            if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1)
                throw new InvalidOperationException("VAD ожидает WAV 16 кГц моно");

            int samplesPerBlock = reader.WaveFormat.SampleRate * 20 / 1000;  // 320
            int bytesPerBlock = samplesPerBlock * 2;                         // 640
            byte[] buffer = new byte[bytesPerBlock];

            double thresholdLinear = Math.Pow(10, silenceThresholdDb / 20) * 32768.0;
            double thresholdPerSampleSquared = thresholdLinear * thresholdLinear;

            string outputPath = Path.Combine(Path.GetTempPath(), $"vad_{Guid.NewGuid()}.wav");
            using var writer = new WaveFileWriter(outputPath, reader.WaveFormat);

            bool isSpeech = false;
            int silenceCounter = 0;
            int speechCounter = 0;
            bool headerWritten = false;

            // Предбуфер: храним последние preSpeechBlocks блоков
            var preBuffer = new Queue<byte[]>();

            while (reader.Read(buffer, 0, bytesPerBlock) == bytesPerBlock)
            {
                double energy = 0;
                for (int i = 0; i < bytesPerBlock; i += 2)
                {
                    short sample = BitConverter.ToInt16(buffer, i);
                    energy += sample * sample;
                }
                energy /= samplesPerBlock;

                bool loud = energy > thresholdPerSampleSquared;

                if (!isSpeech)
                {
                    // Сохраняем блок в кольцевой предбуфер
                    byte[] blockCopy = new byte[bytesPerBlock];
                    Array.Copy(buffer, blockCopy, bytesPerBlock);
                    preBuffer.Enqueue(blockCopy);
                    if (preBuffer.Count > preSpeechBlocks)
                        preBuffer.Dequeue();  // выкидываем старые, оставляя только preSpeechBlocks перед текущим

                    if (loud)
                    {
                        speechCounter++;
                        if (speechCounter >= minSpeechBlocks)
                        {
                            // Переход в речь: сначала сбрасываем предбуфер
                            isSpeech = true;
                            speechCounter = 0;
                            silenceCounter = 0;

                            // Пишем блоки из предбуфера (то, что было перед речью)
                            foreach (var prevBlock in preBuffer)
                            {
                                writer.Write(prevBlock, 0, bytesPerBlock);
                            }
                            preBuffer.Clear();

                            // Пишем текущий блок
                            writer.Write(buffer, 0, bytesPerBlock);
                            headerWritten = true;
                        }
                    }
                    else
                    {
                        speechCounter = 0;
                    }
                }
                else
                {
                    writer.Write(buffer, 0, bytesPerBlock);
                    if (!loud)
                    {
                        silenceCounter++;
                        if (silenceCounter >= minSilenceBlocks)
                        {
                            isSpeech = false;
                            silenceCounter = 0;
                            speechCounter = 0;
                        }
                    }
                    else
                    {
                        silenceCounter = 0;
                    }
                }
            }

            if (!headerWritten)
            {
                writer.Dispose();
                File.Delete(outputPath);
                return wavPath;
            }

            return outputPath;
        }
    }
}