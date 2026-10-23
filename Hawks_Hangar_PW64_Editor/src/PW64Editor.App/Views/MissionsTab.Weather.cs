using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PW64Editor.App.Services;
using PW64Editor.Core.Missions;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.App.Views;

/// <summary>The section "Weather and view" of the Missions tab: time of day, snow and rain, fog colour, fog, view distance, wind.</summary>
public partial class MissionsTab
{
    /// <summary>The Cannonball target whose weather is shown (0–3).</summary>
    private int _weatherTarget;

    /// <summary>The draft whose weather is edited: the mission, or for a Cannonball level the chosen target.</summary>
    private MissionDraft? WeatherDraft(MissionDraft draft)
    {
        if (draft.Vehicle != Vehicle.Cannonball)
        {
            return draft;
        }

        MissionInfo? target = SelectedGroup.FirstOrDefault(m => m.Id.Test == _weatherTarget)
            ?? _missions.FirstOrDefault(m => m.Id == new TestId(draft.Mission.Id.Class, Vehicle.Cannonball, _weatherTarget));
        return target is null ? draft : Draft(target);
    }

    private static string CompassName(float degrees)
    {
        string[] names = [L.T("north"), L.T("north-east"), L.T("east"), L.T("south-east"), L.T("south"), L.T("south-west"), L.T("west"), L.T("north-west")];
        return names[(int)Math.Round(((degrees % 360) + 360) % 360 / 45) % 8];
    }

    private static ComboBoxItem ColorItem(string text, uint rgb, object? tag)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Border
        {
            Width = 22,
            Height = 14,
            Margin = new Thickness(0, 0, 8, 0),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)),
        });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return new ComboBoxItem { Content = panel, Tag = tag };
    }

    /// <summary>Fills a colour list: the colours of the game and more; a colour that is not in the list is added as its own entry.</summary>
    private static void FillColors(ComboBox box, uint selected, (string Text, uint Rgb)? first)
    {
        box.Items.Clear();
        if (first is { } f)
        {
            box.Items.Add(ColorItem(f.Text, f.Rgb, null));
        }

        foreach (WeatherColor color in WeatherCatalog.Colors)
        {
            box.Items.Add(ColorItem(L.T(color.Name), color.Rgb, color.Rgb));
        }

        if (first is null && !WeatherCatalog.Colors.Any(c => c.Rgb == selected))
        {
            box.Items.Add(ColorItem(L.F("Own colour {0}", WeatherCatalog.Hex(selected)), selected, selected));
        }
    }

    private static void Select(ComboBox box, object? tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, tag)) ?? box.Items.OfType<ComboBoxItem>().FirstOrDefault();

    private void ShowWeather(MissionDraft shown)
    {
        bool cannonball = shown.Vehicle == Vehicle.Cannonball;
        WeatherTargetPanel.Visibility = cannonball ? Visibility.Visible : Visibility.Collapsed;
        if (cannonball)
        {
            WeatherTargetBox.ItemsSource = Enumerable.Range(0, 4)
                .Select(t => new ChoiceItem<int>(t, MissionCatalog.TestName(Vehicle.Cannonball, t)))
                .ToList();
            WeatherTargetBox.SelectedIndex = Math.Clamp(_weatherTarget, 0, 3);
        }

        MissionDraft draft = WeatherDraft(shown) ?? shown;
        int island = draft.Comm.Island;

        WeatherBox.Items.Clear();
        foreach (WeatherCondition condition in WeatherCatalog.Conditions)
        {
            bool available = WeatherCatalog.IsAvailable(island, condition);
            WeatherBox.Items.Add(new ComboBoxItem
            {
                Content = !available ? L.F("{0} (not on this island)", L.T(WeatherCatalog.Name(condition)))
                    : WeatherCatalog.IsOriginal(island, condition) ? L.T(WeatherCatalog.Name(condition))
                    : L.F("{0} (new on this island)", L.T(WeatherCatalog.Name(condition))),
                Tag = condition,
                IsEnabled = available || condition == draft.Weather,
            });
        }

        Select(WeatherBox, draft.Weather);

        EffectBox.Items.Clear();
        EffectBox.Items.Add(new ComboBoxItem { Content = L.T("Off"), Tag = WeatherEffect.None });
        EffectBox.Items.Add(new ComboBoxItem { Content = L.T("Snow"), Tag = WeatherEffect.Snow });
        EffectBox.Items.Add(new ComboBoxItem { Content = L.T("Rain"), Tag = WeatherEffect.Rain });
        Select(EffectBox, draft.Effect);
        bool effect = draft.Effect != WeatherEffect.None;
        FallSpeedSlider.Value = draft.FallSpeed;
        DensitySlider.Value = draft.Density;
        FillColors(EffectColorBox, draft.EffectColor, null);
        Select(EffectColorBox, draft.EffectColor);
        foreach (UIElement element in new UIElement[] { FallSpeedLabel, FallSpeedSlider, FallSpeedText, DensityLabel, DensitySlider, DensityText, EffectColorLabel, EffectColorBox })
        {
            element.IsEnabled = effect;
        }

        bool sunset = draft.Weather == WeatherCondition.Sunset;
        uint original = WeatherCatalog.OriginalFogColor(island, draft.Weather);
        FillColors(FogColorBox, draft.FogColor, (L.T("The environment's colour"), original));
        if (!draft.FogColorIsOriginal && !WeatherCatalog.Colors.Any(c => c.Rgb == draft.FogColor))
        {
            FogColorBox.Items.Add(ColorItem(L.F("Own colour {0}", WeatherCatalog.Hex(draft.FogColor)), draft.FogColor, draft.FogColor));
        }

        Select(FogColorBox, draft.FogColorIsOriginal ? null : draft.FogColor);
        FogColorBox.IsEnabled = !sunset;
        SkyTintBox.IsChecked = draft.SkyTint;
        SkyTintBox.IsEnabled = !sunset;
        FogColorNote.Text = sunset
            ? L.T("With a sunset the game sets the fog colour itself in every frame, from the direction of view (warm towards the sun), so it cannot be changed here.")
            : L.T("The fog colour tints the distant landscape and the band at the horizon. With \"Tint the sky\" the sky dome and the background take on the colour as well (in its brightness).");

        ViewSlider.Value = draft.ViewDistance;
        FogAlignedBox.IsChecked = draft.FogAligned;
        FogStartSlider.Maximum = draft.ViewDistance;
        FogStartSlider.Value = draft.FogStart;
        FogDensitySlider.Value = draft.FogDensity;
        foreach (UIElement element in new UIElement[] { FogStartLabel, FogStartSlider, FogStartText, FogDensityLabel, FogDensitySlider, FogDensityText })
        {
            element.IsEnabled = !draft.FogAligned;
        }

        (float direction, float strength) = draft.Wind;
        WindDirectionSlider.Value = Math.Round(direction / 5) * 5 % 360;
        WindStrengthSlider.Value = Math.Min(strength, WindStrengthSlider.Maximum);
        WindDirectionRangeSlider.Value = draft.Comm.WindDirectionRange;
        WindDirectionRateSlider.Value = draft.Comm.WindDirectionRate;
        WindSpeedRangeSlider.Value = draft.Comm.WindSpeedRange;
        WindSpeedRateSlider.Value = draft.Comm.WindSpeedRate;

        var notes = new List<string>();
        if (draft.Vehicle == Vehicle.SkyDiving)
        {
            notes.Add(L.T("Sky Diving: above the clouds the sky is always clear; the weather shows below them."));
        }

        if (effect)
        {
            notes.Add(L.T("Snow and rain are drawn into the finished picture, like the game's snow: emulators that do not copy the frame buffer back (for example some HLE graphics plugins) do not show them."));
        }

        WeatherNote.Text = string.Join(" ", notes);
        WeatherNote.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowWeatherValues(draft);
    }

    /// <summary>The numbers next to the sliders.</summary>
    private void ShowWeatherValues(MissionDraft draft)
    {
        FallSpeedText.Text = L.F("{0} m/s", FormatNumber(draft.FallSpeed));
        DensityText.Text = draft.Effect == WeatherEffect.Rain ? L.F("{0} drops", draft.Density) : L.F("{0} flakes", draft.Density);
        ViewText.Text = L.F("{0} m", FormatMeters(draft.ViewDistance));
        FogStartText.Text = L.F("{0} m", FormatMeters(draft.FogStart));
        FogDensityText.Text = L.F("{0} %", FormatMeters(draft.FogDensity));
        (float direction, float strength) = draft.Wind;
        WindDirectionText.Text = strength == 0 ? L.T("no wind") : L.F("{0}° ({1})", FormatMeters(direction), CompassName(direction));
        WindStrengthText.Text = L.F("{0} m/s", FormatNumber((float)Math.Round(strength, 2)));
        WindDirectionRangeText.Text = L.F("±{0}°", FormatMeters(draft.Comm.WindDirectionRange));
        WindDirectionRateText.Text = L.F("{0}°/s", FormatMeters(draft.Comm.WindDirectionRate));
        WindSpeedRangeText.Text = L.F("±{0} m/s", FormatNumber(draft.Comm.WindSpeedRange));
        WindSpeedRateText.Text = L.F("{0}", FormatNumber(draft.Comm.WindSpeedRate));
    }

    private MissionDraft? EditedWeather => SelectedDraft is { } draft ? WeatherDraft(draft) : null;

    /// <summary>A slider changed: the value goes into the draft, only the numbers are shown again (the slider keeps the mouse).</summary>
    private void OnWeatherSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_showing || EditedWeather is not { } draft)
        {
            return;
        }

        if (sender == FallSpeedSlider)
        {
            draft.SetFallSpeed((float)FallSpeedSlider.Value);
        }
        else if (sender == DensitySlider)
        {
            draft.SetDensity((int)DensitySlider.Value);
        }
        else if (sender == ViewSlider)
        {
            draft.SetViewDistance((float)ViewSlider.Value);
            _showing = true;
            FogStartSlider.Maximum = draft.ViewDistance;
            FogStartSlider.Value = draft.FogStart;
            _showing = false;
        }
        else if (sender == FogStartSlider || sender == FogDensitySlider)
        {
            draft.SetFog((float)FogStartSlider.Value, (float)FogDensitySlider.Value);
        }
        else if (sender == WindDirectionSlider || sender == WindStrengthSlider)
        {
            draft.SetWind((float)WindDirectionSlider.Value, (float)WindStrengthSlider.Value);
        }
        else
        {
            draft.SetWindVariation((float)WindSpeedRangeSlider.Value, (float)WindSpeedRateSlider.Value,
                (float)WindDirectionRangeSlider.Value, (float)WindDirectionRateSlider.Value);
        }

        ShowWeatherValues(draft);
        UndoButton.IsEnabled = true;
        UnsavedChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnWeatherTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || WeatherTargetBox.SelectedItem is not ChoiceItem<int> item || SelectedDraft is not { } draft)
        {
            return;
        }

        _weatherTarget = item.Value;
        _showing = true;
        try
        {
            ShowWeather(draft);
        }
        finally
        {
            _showing = false;
        }
    }

    private void OnWeatherChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || EditedWeather is not { } draft || WeatherBox.SelectedItem is not ComboBoxItem { Tag: WeatherCondition condition })
        {
            return;
        }

        draft.SetWeather(condition);
        Changed(SelectedDraft!);
    }

    private void OnEffectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || EditedWeather is not { } draft || EffectBox.SelectedItem is not ComboBoxItem { Tag: WeatherEffect effect })
        {
            return;
        }

        draft.SetEffect(effect);
        Changed(SelectedDraft!);
    }

    private void OnEffectColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || EditedWeather is not { } draft || EffectColorBox.SelectedItem is not ComboBoxItem { Tag: uint rgb })
        {
            return;
        }

        draft.SetEffectColor(rgb);
        Changed(SelectedDraft!);
    }

    private void OnFogColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || EditedWeather is not { } draft || FogColorBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        draft.SetFogColor(item.Tag as uint?);
        Changed(SelectedDraft!);
    }

    private void OnSkyTintChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || EditedWeather is not { } draft)
        {
            return;
        }

        draft.SetSkyTint(SkyTintBox.IsChecked == true);
        Changed(SelectedDraft!);
    }

    private void OnFogAlignedChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || EditedWeather is not { } draft)
        {
            return;
        }

        draft.SetFogAligned(FogAlignedBox.IsChecked == true);
        Changed(SelectedDraft!);
    }
}
