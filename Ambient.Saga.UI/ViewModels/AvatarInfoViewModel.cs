using Ambient.Domain;
using Ambient.Domain.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ambient.Saga.Presentation.UI.ViewModels;

public partial class AvatarInfoViewModel : ObservableObject
{
    [ObservableProperty]
    private IWorld _currentWorld;

    [ObservableProperty]
    private AvatarBase? _playerAvatar;

    [ObservableProperty]
    private double _avatarLatitude;

    [ObservableProperty]
    private double _avatarLongitude;

    [ObservableProperty]
    private int _avatarElevation;

    [ObservableProperty]
    private bool _hasAvatarPosition;

    [ObservableProperty]
    private bool _hasHeightMapImage;

    public string CurrencyName => CurrentWorld?.WorldConfiguration?.CurrencyName ?? "Coin";

    public void UpdateWorld(IWorld? world)
    {
        CurrentWorld = world;
        OnPropertyChanged(nameof(CurrencyName));
    }

    public void UpdatePlayerAvatar(AvatarBase? avatar)
    {
        PlayerAvatar = avatar;
    }

    public void UpdateAvatarPosition(double latitude, double longitude, int elevation, bool hasPosition)
    {
        AvatarLatitude = latitude;
        AvatarLongitude = longitude;
        AvatarElevation = elevation;
        HasAvatarPosition = hasPosition;
    }

    public void UpdateHeightMapStatus(bool hasHeightMap)
    {
        HasHeightMapImage = hasHeightMap;
    }
}
