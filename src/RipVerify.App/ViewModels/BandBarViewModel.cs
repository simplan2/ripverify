using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace RipVerify.App.ViewModels;

public partial class BandBarViewModel : ObservableObject
{
    [ObservableProperty]
    private double _height;

    [ObservableProperty]
    private double _valueDb;

    public BandBarViewModel(double height, double valueDb)
    {
        _height = height;
        _valueDb = valueDb;
    }
}
