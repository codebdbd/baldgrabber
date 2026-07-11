using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BaldGrabber.Models;

public partial class VideoQualityOption : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private string _id = string.Empty;
    public string Id { get => _id; set => SetProperty(ref _id, value); }

    private string _name = string.Empty;
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _description = string.Empty;
    public string Description { get => _description; set => SetProperty(ref _description, value); }

    private bool _isAvailable = true;
    public bool IsAvailable { get => _isAvailable; set => SetProperty(ref _isAvailable, value); }
}
