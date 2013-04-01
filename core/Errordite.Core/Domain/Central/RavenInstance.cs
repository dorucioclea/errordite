﻿
using Errordite.Core.Configuration;
using ProtoBuf;

namespace Errordite.Core.Domain.Central
{
    [ProtoContract]
    public class RavenInstance
    {
        [ProtoMember(1)]
        public string Id { get; set; }
        /// <summary>
        /// Endpoint for this Raven server
        /// </summary>
        [ProtoMember(2)]
        public string RavenUrl { get; set; }
        /// <summary>
        /// Indicates this is the active server, all new organisations should be added to this server
        /// </summary>
        [ProtoMember(3)]
        public bool Active { get; set; }
        /// <summary>
        /// Is the instance where the Master Errordite database lives?
        /// </summary>
        [ProtoMember(4)]
        public bool IsMaster { get; set; }
        /// <summary>
        /// Endpoint for this Raven server
        /// </summary>
        [ProtoMember(5)]
        public string ReceptionHttpEndpoint { get; set; }
        /// <summary>
        /// Reception service queue address for this instance
        /// </summary>
        [ProtoMember(6)]
        public string ReceptionQueueAddress { get; set; }

        private static readonly RavenInstance _master = new RavenInstance
        {
            Active = true,
            IsMaster = true,
            Id = "RavenInstances/1",
            ReceptionHttpEndpoint = ErrorditeConfiguration.Current.ReceptionHttpEndpoint,
            ReceptionQueueAddress = ErrorditeConfiguration.Current.ReceptionQueueName
        };

        public static RavenInstance Master()
        {
            return _master;
        }
    }
}