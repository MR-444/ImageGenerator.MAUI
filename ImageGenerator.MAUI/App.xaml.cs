using ImageGenerator.MAUI.Views;

namespace ImageGenerator.MAUI
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            // Shell is your MainPage
            MainPage = new AppShell();
        }
/*
        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
        */
    }
}