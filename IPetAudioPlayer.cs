using System.Threading.Tasks;

namespace QuackDuck;

internal interface IPetAudioPlayer
{
    Task PlayAsync(string path);
}
