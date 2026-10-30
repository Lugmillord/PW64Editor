using System.Windows;
using System.Windows.Controls;
using PW64Editor.App.Services;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// Explains how the game scores photos (Missions tab, section "Photos" and the photo row of the points): what becomes the
/// photo's subject, how its points are computed and which photos count (Level_Editor_Prep.md section 6.1).
/// </summary>
public partial class PhotoScoringWindow : Window
{
    private PhotoScoringWindow()
    {
        InitializeComponent();
        (string Heading, string Text)[] topics =
        [
            (L.T("Which object a photo shows"),
                L.T("When a photo is taken, the game looks at the mission's photo subjects that are in front of the camera and roughly in the picture. The nearest of them becomes the photo's subject. A subject nearer to the camera therefore takes the photo even if another one was aimed at. If none is in the picture, the photo has no subject and gives no points.")),
            (L.T("The points of a photo"),
                L.T("Points of the subject × centring × share for the distance.") + "\n"
                + L.T("Centring: all points when the subject is exactly in the middle of the picture, fewer towards the edge of the middle half of the picture, nothing outside it (the subject has to be in the middle half across and up and down).") + "\n"
                + L.T("Distance: the curve of the subject gives the share of the points for the distance between the camera and the subject (in meters as the game counts them).") + "\n"
                + L.T("Direction (if set): the photo only counts when the camera looks in that direction, within the range ± around it. The direction is the camera's heading as the game counts it, 0 to 360 degrees (the original fountain photo of Class B uses 90° ± 130°).")),
            (L.T("Which photos count"),
                L.T("Up to ten photos per flight. Of all photos of a subject only the best one counts; the photo points of the flight are the sum of the best photo of each subject. After the flight up to six photos can be kept in the album.") + "\n"
                + L.T("With subjects of its own a photo counts for the object that gives the most points in it (the original game takes the nearest object in the picture, even if another one is in the middle). A whale of the pod counts only while it is above the water.")),
            (L.T("Subjects and objects"),
                L.T("The space shuttle, the ferry, Missi the lake monster, the whale, the fountain and the flame of the oil plant exist once on their island. Of the whale pod, the planes, the model gliders and the boats an island has several: a subject can be any of them or one of them.")),
            (L.T("The original missions"),
                L.T("The original photo missions store each subject's points as two numbers whose product gives the points (for example 7.746 × 7.746 = 60). The editor shows one number of points and a curve in percent. As long as the subjects of a mission are not changed, the game's own code scores them; changed subjects are scored by the editor's game code in the same way.")),
            (L.T("Photo missions"),
                L.T("In a photo mission the HUD says \"Photo\" when a photo gives points, the album shows the points of its photos, and the radar marks the island's main subjects that the mission photographs. Only then are the photo points shown in the album; the score counts them in every mission.")),
            (L.T("Radar and map"),
                L.T("A mission with photo subjects of its own marks every object of its subjects with points on the radar and on the pause menu's map, in every mission, photo mission or not; a subject for any object of its type marks all of them. On the map the six main subjects have their names, the others say \"Target\". The map of the test screen shows only the main subjects, as the other objects are not there yet. Model gliders, boats and planes appear on the radar once they move (the game moves them only when the player is near enough).")),
            (L.T("Sample photos"),
                L.T("The test screen's \"Sample photo\" button and the pause menu show sample photos of a photo mission. A mission with photo subjects of its own gets one per subject with points: the camera at the distance, direction and height angle set for the subject, looking at the object at the start of its movement (the whale, Missi and the whales of the pod above the water). Without own subjects they are the island's sample photos of the original game. Every sample photo is drawn when it is shown, which takes a few seconds (\"Please wait...\").")),
        ];
        foreach ((string heading, string text) in topics)
        {
            SectionsPanel.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 10, 0, 4) });
            SectionsPanel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        }
    }

    /// <summary>Shows the explanation as a modal window.</summary>
    public static void ShowHelp(Window owner) => new PhotoScoringWindow { Owner = owner }.ShowDialog();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
