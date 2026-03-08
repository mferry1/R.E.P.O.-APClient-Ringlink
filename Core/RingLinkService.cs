using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Converters;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


// This mimics the whole structure of the DeathLink and DeathLinkService at
// https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net/blob/main/Archipelago.MultiClient.Net/BounceFeatures/DeathLink/
// If Archipelago.MultiClient.Net adds a service for it in the future (highly likely), we can get rid of this and use theirs

namespace RepoAP.Core {

	public class RingLink : IEquatable<RingLink> {
		
		public int DeltaCurreny { get; }
		//
		// Summary:
		//     The Timestamp of the created RingLink object
		public DateTime Timestamp { get; internal set; }

		//
		// Summary:
		//     The name of the player who sent the RingLink
		public string Source { get; }

		//
		// Summary:
		//     The full text to print for players receiving the RingLink. Can be null
		public string Cause { get; }

		//
		// Summary:
		//     A RingLink object that gets sent and received via bounce packets.
		//
		// Parameters:
		//	 deltaValue:
		//	   How much the currency is changing by. This is a delata so that race condintions don't wipe
		//	   each other out
		//
		//   sourcePlayer:
		//     Name of the player sending the RingLink
		//
		//   cause:
		//     Optional reason for the RingLink. Since this is optional it should generally
		//     include a name as if this entire text is what will be displayed
		public RingLink(int deltaCurrenty, string sourcePlayer, string cause = null) {
			DeltaCurreny = deltaCurrenty;
			Timestamp = DateTime.UtcNow;
			Source = sourcePlayer;
			Cause = cause;
		}

		internal static bool TryParse(Dictionary<string, JToken> data, out RingLink ringLink) {
			try {
				if (!data.TryGetValue("deltaCurrency", out var deltaCurrVal) ||
					!data.TryGetValue("time", out var timeVal) ||
					!data.TryGetValue("source", out var sourceVal)) {
					ringLink = null;
					return false;
				}

				string cause = null;
				if (data.TryGetValue("cause", out var causeVal)) {
					cause = causeVal.ToString();
				}

				ringLink = new RingLink(deltaCurrVal.Value<int>(), sourceVal.ToString(), cause) {
					Timestamp = UnixTimeConverter.UnixTimeStampToDateTime(timeVal.ToObject<double>())
				};
				return true;
			}
			catch {
				ringLink = null;
				return false;
			}
		}

		public bool Equals(RingLink other) {
			if ((object)other == null) {
				return false;
			}

			if ((object)this == other) {
				return true;
			}

			if (Source == other.Source && DeltaCurreny == other.DeltaCurreny && 
				Timestamp.Date.Equals(other.Timestamp.Date) && 
				Timestamp.Hour == other.Timestamp.Hour && 
				Timestamp.Minute == other.Timestamp.Minute &&
				Timestamp.Second == other.Timestamp.Second) 
			{
				return true;
			}

			return false;
		}

		public override bool Equals(object obj) {
			if (obj == null) {
				return false;
			}

			if (obj.GetType() != GetType()) {
				return false;
			}

			if (this == obj as RingLink) {
				return true;
			}

			return Equals((RingLink)obj);
		}

		public override int GetHashCode() {
			return (Timestamp.GetHashCode() * 397) ^ ((Source != null) ? Source.GetHashCode() : 0);
		}

		public static bool operator ==(RingLink lhs, RingLink rhs) {
			return lhs?.Equals(rhs) ?? ((object)rhs == null);
		}

		public static bool operator !=(RingLink lhs, RingLink rhs) {
			return !(lhs == rhs);
		}
	}

	public class RingLinkService {
		//
		// Summary:
		//     event for clients to hook into and decide what to do with the received RingLink
		public delegate void RingLinkReceivedHandler(RingLink ringLink);

		private readonly IArchipelagoSocketHelper socket;

		private readonly IConnectionInfoProvider connectionInfoProvider;

		private RingLink lastSendRingLink;

		//
		// Summary:
		//     whenever one is received from the server as a bounce packet.
		public event RingLinkReceivedHandler OnRingLinkReceived;

		internal RingLinkService(IArchipelagoSocketHelper socket, IConnectionInfoProvider connectionInfoProvider) {
			this.socket = socket;
			this.connectionInfoProvider = connectionInfoProvider;
			socket.PacketReceived += OnPacketReceived;
		}

		private void OnPacketReceived(ArchipelagoPacketBase packet) {
			if (packet is BouncedPacket bouncedPacket && bouncedPacket.Tags.Contains("RingLink") && RingLink.TryParse(bouncedPacket.Data, out var ringLink) && (!(lastSendRingLink != null) || !(lastSendRingLink == ringLink)) && this.OnRingLinkReceived != null) {
				this.OnRingLinkReceived(ringLink);
			}
		}

		//
		// Summary:
		//     Formats and sends a Bounce packet using the provided ringLink object.
		//
		// Parameters:
		//   ringLink:
		//     the information of the ringLink which occurred. Must at least contain the RingLink.Source.
		//
		//
		// Exceptions:
		//   T:Archipelago.MultiClient.Net.Exceptions.ArchipelagoSocketClosedException:
		//     The websocket connection is not alive
		public void SendRingLink(RingLink ringLink) {
			BouncePacket bouncePacket = new BouncePacket {
				Tags = new List<string> { "RingLink" },
				Data = new Dictionary<string, JToken>
				{
				{
					"time",
					ringLink.Timestamp.ToUnixTimeStamp()
				},
				{ "source", ringLink.Source }
			}
			};
			if (ringLink.Cause != null) {
				bouncePacket.Data.Add("cause", ringLink.Cause);
			}

			lastSendRingLink = ringLink;
			socket.SendPacketAsync(bouncePacket);
		}

		//
		// Summary:
		//     Adds "RingLink" to your Archipelago.MultiClient.Net.ArchipelagoSession's tags
		//     RingLinkService.OnRingLinkReceived
		//     events
		public void EnableRingLink() {
			if (Array.IndexOf(connectionInfoProvider.Tags, "RingLink") == -1) {
				connectionInfoProvider.UpdateConnectionOptions(connectionInfoProvider.Tags.Concat(new string[1] { "RingLink" }).ToArray());
			}
		}

		//
		// Summary:
		//     Removes the "RingLink" tag from your Archipelago.MultiClient.Net.ArchipelagoSession
		//     and opts out of further RingLinkService.OnRingLinkReceived
		//     events
		public void DisableRingLink() {
			if (Array.IndexOf(connectionInfoProvider.Tags, "RingLink") != -1) {
				connectionInfoProvider.UpdateConnectionOptions(connectionInfoProvider.Tags.Where((string t) => t != "RingLink").ToArray());
			}
		}
	}
	public static class RingLinkProvider {
		// ReSharper disable once UnusedMember.Global
		/// <summary>
		///     creates and returns a <see cref="RingLinkService"/> for this <paramref name="session"/>.
		/// </summary>
		public static RingLinkService CreateRingLinkService(this ArchipelagoSession session) =>
			new RingLinkService(session.Socket, session.ConnectionInfo);
	}
}
