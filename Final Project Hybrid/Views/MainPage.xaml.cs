using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Data.Json;
using Windows.Web.Http;
using Windows.UI.Xaml.Controls;
using Windows.Devices.Geolocation;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.Services.Maps;
using Windows.UI.Xaml.Controls.Maps;
using Windows.UI;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using System.Collections.Generic;

namespace Final_Project_Hybrid.Views
{
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        // GPS Stuff
        Geolocator GPS;
        Geoposition userPosition;

        // Route details
        JsonObject routeDetails;
        JsonObject startLocation;
        JsonObject endLocation;

        // Bing VirtualEarth Stuff
        const string apiKey = "APIKEY";
        String baseURL = "http://dev.virtualearth.net/REST/V1/Routes/Driving?output=json&key=" + apiKey;

        public MainPage()
        {
            InitializeComponent();

            // Setting up the GPS
            GPS = new Geolocator();
            GPS.DesiredAccuracy = PositionAccuracy.Default;

            // Setting up the map
            SetupMap();
        }

        private async void SetupMap()
        {
            try
            {
                // Getting the user location
                userPosition = await GPS.GetGeopositionAsync();
                BasicGeoposition snPosition = new BasicGeoposition
                {
                    Latitude = userPosition.Coordinate.Point.Position.Latitude,
                    Longitude = userPosition.Coordinate.Point.Position.Longitude
                };

                // Converting it to a geopoint
                Geopoint snPoint = new Geopoint(snPosition);

                await map.TrySetViewAsync(snPoint);
                await map.TryZoomToAsync(14);
            }
            catch (Exception)
            {
                // Otherwise show a notification
                var content = new ToastContentBuilder()
                    .AddToastActivationInfo("no_routes_found", ToastActivationType.Foreground)
                    .AddText("Could not get your location...")
                    .GetToastContent();

                var notification = new ToastNotification(content.GetXml());

                ToastNotificationManager.CreateToastNotifier().Show(notification);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T storage, T value, [CallerMemberName]string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            OnPropertyChanged(propertyName);
        }

        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // Find the route
        private async void Route_button_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            // If both places are set...
            if (from_text_box.Text.Trim().Length > 0
                && to_text_box.Text.Trim().Length > 0)
            {
                // Start calculationg the route
                LoadingControl.IsLoading = true;
                route_button.IsEnabled = false;
                ClearLastRoute();
                await GetRouteDetails();
                if (routeDetails != null) await DrawRoute();
                LoadingControl.IsLoading = false;
                route_button.IsEnabled = true;
            }
        }

        private void ClearLastRoute()
        {
            // Removing last route
            map.Routes.Clear();
        }

        private async Task GetRouteDetails()
        {
            // Getting the request
            var client = new HttpClient();
            JsonValue jsonValue;
            JsonObject root;

            String request = baseURL +
                "&wp.0=" + from_text_box.Text +
                "&wp.1=" + to_text_box.Text;

            if (tolls_toggle_button.IsOn == false) request += "&avoid=minimizeTolls";

            HttpResponseMessage response = await client.GetAsync(new Uri(request));
            var jsonString = await response.Content.ReadAsStringAsync();

            bool couldParse = JsonValue.TryParse(jsonString, out jsonValue);
            // If the respone code was OK...
            if (couldParse && response.StatusCode == HttpStatusCode.Ok)
            {
                // Parse the json into an object
                root = jsonValue.GetObject();

                routeDetails = root
                    .GetNamedArray("resourceSets")
                    .GetObjectAt(0)
                    .GetNamedArray("resources")
                    .GetObjectAt(0)
                    .GetNamedArray("routeLegs")
                    .GetObjectAt(0);

            }
            else
            {
                // Otherwise show a notification
                var content = new ToastContentBuilder()
                    .AddToastActivationInfo("no_routes_found", ToastActivationType.Foreground)
                    .AddText("Could not find any routes for the places you provided...")
                    .GetToastContent();

                var notification = new ToastNotification(content.GetXml());

                ToastNotificationManager.CreateToastNotifier().Show(notification);
            }
        }

        private async Task DrawRoute()
        {
            // Getting the start and end points from the response
            startLocation = routeDetails.GetNamedObject("startLocation");
            double[] startCoordinates =
                {
                startLocation.GetNamedObject("point").GetNamedArray("coordinates").GetNumberAt(0),
                startLocation.GetNamedObject("point").GetNamedArray("coordinates").GetNumberAt(1)
            };
            BasicGeoposition startPoint = new BasicGeoposition
            {
                Latitude = startCoordinates[0],
                Longitude = startCoordinates[1]
            };

            endLocation = routeDetails.GetNamedObject("endLocation");
            double[] endCoordinates =
                {
                endLocation.GetNamedObject("point").GetNamedArray("coordinates").GetNumberAt(0),
                endLocation.GetNamedObject("point").GetNamedArray("coordinates").GetNumberAt(1)
            };
            BasicGeoposition endPoint = new BasicGeoposition
            {
                Latitude = endCoordinates[0],
                Longitude = endCoordinates[1]
            };

            MapRouteFinderResult routeResult;

            if (how_bar_item.Title == "Drive there")
            {
                // Getting every direction from the response
                JsonArray itineraryItems = routeDetails.GetNamedArray("itineraryItems");
                List<EnhancedWaypoint> geopints = new List<EnhancedWaypoint> { };

                for (uint i = 0; i < itineraryItems.Count; i++)
                {
                    JsonArray jsonCoordinates = itineraryItems
                        .GetObjectAt(i)
                        .GetNamedObject("maneuverPoint")
                        .GetNamedArray("coordinates");

                    double[] coordinates = {
                    jsonCoordinates.GetNumberAt(0),
                    jsonCoordinates.GetNumberAt(1)
                };

                    Geopoint geopoint = new Geopoint(
                        new BasicGeoposition
                        {
                            Latitude = coordinates[0],
                            Longitude = coordinates[1]
                        });

                    EnhancedWaypoint waypoint;
                    if (i == 0 || i == itineraryItems.Count - 1) waypoint = new EnhancedWaypoint(geopoint, WaypointKind.Stop);
                    else waypoint = new EnhancedWaypoint(geopoint, WaypointKind.Via);


                    geopints.Add(waypoint);
                }

                // Assigning it to the map route object
                routeResult = await MapRouteFinder
                    .GetDrivingRouteFromEnhancedWaypointsAsync(geopints);
            } else
                // If Walk there was selected...
                routeResult = await MapRouteFinder.GetWalkingRouteAsync(
                                                        new Geopoint(startPoint),
                                                        new Geopoint(endPoint));


            // If the app could find a route...
            if (routeResult.Status == MapRouteFinderStatus.Success)
            {
                // Draw the route
                MapRouteView viewOfRoute = new MapRouteView(routeResult.Route);
                viewOfRoute.RouteColor = Colors.Coral;
                viewOfRoute.OutlineColor = Colors.Black;

                map.Routes.Add(viewOfRoute);

                await map.TrySetViewBoundsAsync(
                      routeResult.Route.BoundingBox,
                      null,
                      MapAnimationKind.Bow);
                await map.TryZoomToAsync(map.ZoomLevel - 0.2);
                

                destination_panel.Visibility = Windows.UI.Xaml.Visibility.Visible;
                destination_name.Text = "Destination: " + endLocation.GetNamedString("name");
                estimated_time.Text = "Estimated travel duration: " + routeResult.Route.EstimatedDuration;
            }
            else
            {
                // Otherwise show a notification
                var content = new ToastContentBuilder()
                    .AddToastActivationInfo("no_routes_found", ToastActivationType.Foreground)
                    .AddText("There was an error while trying to calculate the route...")
                    .GetToastContent();

                var notification = new ToastNotification(content.GetXml());

                ToastNotificationManager.CreateToastNotifier().Show(notification);
            }
        }

        private void check_text_boxes_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Enable the find route button if both textboxes are not empty
            if (from_text_box.Text.Trim().Length > 0
                && to_text_box.Text.Trim().Length > 0)
            {
                route_button.IsEnabled = true;
            }
        }

        private void how_bar_sub_item_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            // Some setting up of the menu...
            if (how_bar_item.Title == "Drive there")
            {
                how_bar_item.Title = "Walk there";
                how_bar_sub_item.Text = "Drive there";
                tolls_toggle_button.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                tolls_toggle_button.IsOn = false;
                menu.Margin = new Windows.UI.Xaml.Thickness(0, 0, 10, 0);
            }
            else
            {
                how_bar_item.Title = "Drive there";
                how_bar_sub_item.Text = "Walk there";
                tolls_toggle_button.Visibility = Windows.UI.Xaml.Visibility.Visible;
                menu.Margin = new Windows.UI.Xaml.Thickness(0);
            }

        }

        private void MenuFlyoutItem_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            // Changing the map style
            MenuFlyoutItem button = sender as MenuFlyoutItem;

            if (button.Name == "aerial") map.Style = MapStyle.Aerial;
            else if (button.Name == "road") map.Style = MapStyle.Road;
            else if (button.Name == "terrain") map.Style = MapStyle.Terrain;
        }
    }
}
