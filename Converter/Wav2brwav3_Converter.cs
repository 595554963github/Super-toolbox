namespace super_toolbox
{
    public class Wav2brwav3_Converter : Wav2brwav1_Converter
    {
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await ExtractAsyncInternal(directoryPath, BrwavEncoding.DSPADPCM, cancellationToken);
        }
    }
}