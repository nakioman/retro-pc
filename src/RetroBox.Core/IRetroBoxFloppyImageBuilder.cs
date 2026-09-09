namespace RetroBox.Core;

public interface IRetroBoxFloppyImageBuilder
{
    void BuildFromZip(string zipPath, string imagePath);
}
