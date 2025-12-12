using System.Threading.Tasks;

namespace QuackDuck;

internal interface IPetAudioPlayer
{
    Task PlayAsync(string path);
    bool Enabled { get; set; }
    double Volume { get; set; }
}
