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

	public class EnergyLink : IEquatable<EnergyLink> {
		
		public int DeltaCurreny { get; }
		//
		// Summary:
		//     The Timestamp of the created EnergyLink object
		public DateTime Timestamp { get; internal set; }

		//
		// Summary:
		//     The name of the player who sent the EnergyLink
		public string Source { get; }

		//
		// Summary:
		//     The full text to print for players receiving the EnergyLink. Can be null
		public string Cause { get; }

		//
		// Summary:
		//     A EnergyLink object that gets sent and received via bounce packets.
		//
		// Parameters:
		//	 deltaValue:
		//	   How much the currency is changing by. This is a delata so that race condintions don't wipe
		//	   each other out
		//
		//   sourcePlayer:
		//     Name of the player sending the EnergyLink
		//
		//   cause:
		//     Optional reason for the EnergyLink. Since this is optional it should generally
		//     include a name as if this entire text is what will be displayed
		public EnergyLink(int deltaCurrenty, string sourcePlayer, string cause = null) {
			DeltaCurreny = deltaCurrenty;
			Timestamp = DateTime.UtcNow;
			Source = sourcePlayer;
			Cause = cause;
		}

		internal static bool TryParse(Dictionary<string, JToken> data, out EnergyLink EnergyLink) {
			try {
				if (!data.TryGetValue("deltaCurrency", out var deltaCurrVal) ||
					!data.TryGetValue("time", out var timeVal) ||
					!data.TryGetValue("source", out var sourceVal)) {
					EnergyLink = null;
					return false;
				}

				string cause = null;
				if (data.TryGetValue("cause", out var causeVal)) {
					cause = causeVal.ToString();
				}

				EnergyLink = new EnergyLink(deltaCurrVal.Value<int>(), sourceVal.ToString(), cause) {
					Timestamp = UnixTimeConverter.UnixTimeStampToDateTime(timeVal.ToObject<double>())
				};
				return true;
			}
			catch {
				EnergyLink = null;
				return false;
			}
		}

		public bool Equals(EnergyLink other) {
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

			if (this == obj as EnergyLink) {
				return true;
			}

			return Equals((EnergyLink)obj);
		}

		public override int GetHashCode() {
			return (Timestamp.GetHashCode() * 397) ^ ((Source != null) ? Source.GetHashCode() : 0);
		}

		public static bool operator ==(EnergyLink lhs, EnergyLink rhs) {
			return lhs?.Equals(rhs) ?? ((object)rhs == null);
		}

		public static bool operator !=(EnergyLink lhs, EnergyLink rhs) {
			return !(lhs == rhs);
		}
	}

	public class EnergyLinkService {
		//
		// Summary:
		//     event for clients to hook into and decide what to do with the received EnergyLink
		public delegate void EnergyLinkReceivedHandler(EnergyLink EnergyLink);

		private readonly IArchipelagoSocketHelper socket;

		private readonly IConnectionInfoProvider connectionInfoProvider;

		private EnergyLink lastSendEnergyLink;

		//
		// Summary:
		//     whenever one is received from the server as a bounce packet.
		public event EnergyLinkReceivedHandler OnEnergyLinkReceived;

		internal EnergyLinkService(IArchipelagoSocketHelper socket, IConnectionInfoProvider connectionInfoProvider) {
			this.socket = socket;
			this.connectionInfoProvider = connectionInfoProvider;
			socket.PacketReceived += OnPacketReceived;
		}

		private void OnPacketReceived(ArchipelagoPacketBase packet) {
			if (packet is BouncedPacket bouncedPacket && bouncedPacket.Tags.Contains("EnergyLink") && 
				EnergyLink.TryParse(bouncedPacket.Data, out var energyLink) && 
				(!(lastSendEnergyLink != null) || !(lastSendEnergyLink == energyLink)) && 
				this.OnEnergyLinkReceived != null) 
			{
				this.OnEnergyLinkReceived(energyLink);
			}
		}

		//
		// Summary:
		//     Formats and sends a Bounce packet using the provided EnergyLink object.
		//
		// Parameters:
		//   EnergyLink:
		//     the information of the EnergyLink which occurred. Must at least contain the EnergyLink.Source.
		//
		//
		// Exceptions:
		//   T:Archipelago.MultiClient.Net.Exceptions.ArchipelagoSocketClosedException:
		//     The websocket connection is not alive
		public void SendEnergyLink(EnergyLink EnergyLink) {
			BouncePacket bouncePacket = new BouncePacket {
				Tags = new List<string> { "EnergyLink" },
				Data = new Dictionary<string, JToken>
				{
				{
					"time",
					EnergyLink.Timestamp.ToUnixTimeStamp()
				},
				{ "source", EnergyLink.Source }
			}
			};
			if (EnergyLink.Cause != null) {
				bouncePacket.Data.Add("cause", EnergyLink.Cause);
			}

			lastSendEnergyLink = EnergyLink;
			socket.SendPacketAsync(bouncePacket);
		}

		//
		// Summary:
		//     Adds "EnergyLink" to your Archipelago.MultiClient.Net.ArchipelagoSession's tags
		//     EnergyLinkService.OnEnergyLinkReceived
		//     events
		public void EnableEnergyLink() {
			if (Array.IndexOf(connectionInfoProvider.Tags, "EnergyLink") == -1) {
				connectionInfoProvider.UpdateConnectionOptions(connectionInfoProvider.Tags.Concat(new string[1] { "EnergyLink" }).ToArray());
			}
		}

		//
		// Summary:
		//     Removes the "EnergyLink" tag from your Archipelago.MultiClient.Net.ArchipelagoSession
		//     and opts out of further EnergyLinkService.OnEnergyLinkReceived
		//     events
		public void DisableEnergyLink() {
			if (Array.IndexOf(connectionInfoProvider.Tags, "EnergyLink") != -1) {
				connectionInfoProvider.UpdateConnectionOptions(connectionInfoProvider.Tags.Where((string t) => t != "EnergyLink").ToArray());
			}
		}
	}

	public static class EnergyLinkProvider {
		// ReSharper disable once UnusedMember.Global
		/// <summary>
		///     creates and returns a <see cref="EnergyLinkService"/> for this <paramref name="session"/>.
		/// </summary>
		public static EnergyLinkService CreateEnergyLinkService(this ArchipelagoSession session) =>
			new EnergyLinkService(session.Socket, session.ConnectionInfo);
	}
}
