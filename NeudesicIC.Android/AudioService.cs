using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Support.V4.App;

namespace NeudesicIC
{
    [Service]
    public class AudioService : Service
    {
        private MediaPlayer mediaPlayer;
        private StopAudioReceiver stopAudioReceiver;

        public override IBinder OnBind(Intent intent)
        {
            return null;
        }

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            string audioUri = intent.GetStringExtra("AudioUri");

            if (!string.IsNullOrEmpty(audioUri))
            {
                mediaPlayer = MediaPlayer.Create(this, Resource.Raw.hazard_alarm);
                mediaPlayer.Start();

                // Ensure the service continues running in the background
                Notification notification = CreateNotification();
                StartForeground(1, notification);
                RegisterStopAudioReceiver();
                return StartCommandResult.Sticky;
            }

            return StartCommandResult.NotSticky;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

            UnregisterReceiver(stopAudioReceiver);

            mediaPlayer?.Release();
            mediaPlayer = null;
        }

        private void RegisterStopAudioReceiver()
        {
            stopAudioReceiver = new StopAudioReceiver(this);
            IntentFilter intentFilter = new IntentFilter("StopAudioAction");
            RegisterReceiver(stopAudioReceiver, intentFilter);
        }

        public void StopAudio()
        {
            if (mediaPlayer != null && mediaPlayer.IsPlaying)
            {
                mediaPlayer.Stop();
            }

            StopForeground(true);
            StopSelf(); // Stop the service
        }

        private Notification CreateNotification()
        {
            // Create a notification channel (required for Android Oreo and above)
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                NotificationChannel channel = new NotificationChannel("hazard_alarm", "Alarm", NotificationImportance.Default);
                NotificationManager notificationManager = (NotificationManager)GetSystemService(NotificationService);
                notificationManager.CreateNotificationChannel(channel);
            }

            // Build the notification
            NotificationCompat.Builder builder = new NotificationCompat.Builder(this, "hazard_alarm")
                .SetContentTitle("Audio Service")
                .SetContentText("Audio is playing in the background")
                .SetOngoing(true); // Keeps the notification ongoing

            return builder.Build();
        }
    }
    public class StopAudioReceiver : BroadcastReceiver
    {
        private readonly AudioService audioService;

        public StopAudioReceiver(AudioService audioService)
        {
            this.audioService = audioService;
        }

        public override void OnReceive(Context context, Intent intent)
        {
            audioService.StopAudio();
        }
    }
}
