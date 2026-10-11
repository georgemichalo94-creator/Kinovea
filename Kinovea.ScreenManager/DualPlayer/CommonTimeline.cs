using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    public class CommonTimeline
    {
        public long LastTime
        {
            get { return commonLastTime; }
        }

        public long FrameTime
        {
            get { return frameTime; }
        }

        Dictionary<Guid, PlayerSyncInfo> syncInfos = new Dictionary<Guid, PlayerSyncInfo>();
        private long commonLastTime;
        private long frameTime;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);


        /// <summary>
        /// Initialize synchro using players current time origins.
        /// </summary>
        public void Initialize(PlayerScreen leftPlayer, PlayerScreen rightPlayer)
        {
            Initialize(new List<PlayerScreen>() { leftPlayer, rightPlayer });
        }

        /// <summary>
        /// Initialize synchro of any number of players using their current time origins.
        /// The time origins are aligned on the latest one: that player starts at common time 0 
        /// and the others have an offset. With "sync by motion" the durations from the time origin 
        /// to the end are matched to the first player.
        /// </summary>
        public void Initialize(IList<PlayerScreen> players)
        {
            syncInfos.Clear();
            frameTime = 0;
            commonLastTime = 0;
            if (players == null || players.Count == 0)
                return;

            PlayerScreen first = players[0];
            long firstDuration = first.LocalLastTime - first.LocalTimeOriginPhysical;

            List<PlayerSyncInfo> infos = new List<PlayerSyncInfo>();
            foreach (PlayerScreen player in players)
            {
                PlayerSyncInfo info = new PlayerSyncInfo();
                info.SyncTime = player.LocalTimeOriginPhysical;
                info.LastTime = player.LocalLastTime;
                info.Scale = 1.0;
                if (PreferencesManager.PlayerPreferences.SyncByMotion && player != first && firstDuration != 0)
                {
                    long duration = info.LastTime - info.SyncTime;
                    if (duration > 0)
                        info.Scale = (double)duration / firstDuration;
                }

                infos.Add(info);
            }

            // Start of each video in common time.
            // The video with the latest time origin starts at 0, the others have an offset.
            // This is what aligns the videos on their respective time origin.
            long maxOrigin = long.MinValue;
            for (int i = 0; i < players.Count; i++)
                maxOrigin = Math.Max(maxOrigin, (long)(players[i].LocalTimeOriginPhysical / infos[i].Scale));

            frameTime = long.MaxValue;
            for (int i = 0; i < players.Count; i++)
            {
                infos[i].Offset = maxOrigin - (long)(players[i].LocalTimeOriginPhysical / infos[i].Scale);
                syncInfos[players[i].Id] = infos[i];
                frameTime = Math.Min(frameTime, (long)(players[i].LocalFrameTime * infos[i].Scale));
            }

            for (int i = 0; i < players.Count; i++)
                commonLastTime = Math.Max(commonLastTime, GetCommonTime(players[i], infos[i].LastTime));
        }
        
        /// <summary>
        /// Converts a common time into a local time for a specific player.
        /// </summary>
        public long GetLocalTime(PlayerScreen player, long commonTime)
        {
            if (!syncInfos.ContainsKey(player.Id))
                return 0;

            return ((long)(commonTime * syncInfos[player.Id].Scale)) - syncInfos[player.Id].Offset;
        }

        /// <summary>
        /// Converts a local time in a player into a common time.
        /// </summary>
        public long GetCommonTime(PlayerScreen player, long localTime)
        {
            if (!syncInfos.ContainsKey(player.Id))
                return 0;

            return syncInfos[player.Id].Offset + ((long)(localTime / syncInfos[player.Id].Scale));
        }

        /// <summary>
        /// Test whether a given common time is outside the range of the player.
        /// </summary>
        public bool IsOutOfBounds(PlayerScreen player, long commonTime)
        {
            if (!syncInfos.ContainsKey(player.Id))
                return true;

            long localTime = GetLocalTime(player, commonTime);
            return localTime < 0 || localTime > syncInfos[player.Id].LastTime;
        }
    }
}
