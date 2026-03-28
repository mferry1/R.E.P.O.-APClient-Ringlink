using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Converters;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static ModulePropSwitch;
using static RepoAP.ArchipelagoConnection;
using static RepoAP.Plugin;


// This mimics the whole structure of the DeathLink and DeathLinkService at
// https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net/blob/main/Archipelago.MultiClient.Net/BounceFeatures/DeathLink/
// If Archipelago.MultiClient.Net adds a service for it in the future (highly likely), we can get rid of this and use theirs

namespace RepoAP.Core {

	public class EnergyLink : IEquatable<EnergyLink> {
		
		public int DeltaCurreny { get; }
		public int DeltaTruckEnergy { get; }
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
		public EnergyLink(int deltaCurrenty, int deltaTruckEnergy, string sourcePlayer, string cause = null) {
			DeltaCurreny = deltaCurrenty;
			DeltaTruckEnergy = deltaTruckEnergy;
			Timestamp = DateTime.UtcNow;
			Source = sourcePlayer;
			Cause = cause;
		}

		internal static bool TryParse(Dictionary<string, JToken> data, out EnergyLink EnergyLink) {
			try {
				if (!data.TryGetValue("deltaCurrency", out var deltaCurrVal) ||
					!data.TryGetValue("deltaTruckEnergy", out var deltaTruckEnergy) ||
					!data.TryGetValue("time", out var timeVal) ||
					!data.TryGetValue("source", out var sourceVal)) {
					EnergyLink = null;
					return false;
				}

				string cause = null;
				if (data.TryGetValue("cause", out var causeVal)) {
					cause = causeVal.ToString();
				}

				EnergyLink = new EnergyLink(deltaCurrVal.Value<int>(), deltaTruckEnergy.Value<int>(), sourceVal.ToString(), cause) {
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

		private readonly IConnectionInfoProvider connectionInfoProvider;

		private EnergyLink lastSendEnergyLink;

		//
		// Summary:
		//     whenever one is received from the server as a bounce packet.
		public event EnergyLinkReceivedHandler OnEnergyLinkReceived;

		internal EnergyLinkService(IArchipelagoSocketHelper socket, IConnectionInfoProvider connectionInfoProvider) {
			//Debug.Log("Creating Energy Link Service");
			this.connectionInfoProvider = connectionInfoProvider;
			socket.PacketReceived += OnPacketReceived;
			//Debug.Log("Created Energy Link Service");
		}

		public void OnPacketReceived(ArchipelagoPacketBase packet) {
			Debug.Log("OnPacketReceived called");

			Debug.Log($"Has handler? {OnEnergyLinkReceived != null}"); 
			if (packet is not BouncedPacket bouncedPacketT)
				Debug.Log($"Packet type: {packet.GetType().Name}");
			else
				Debug.Log($"Tags contains EnergyLink? {bouncedPacketT.Tags.Contains("EnergyLink")}");
			if(this.OnEnergyLinkReceived == null)
				Debug.Log("OnEnergyLinkReceived is null");

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
				Games = new List<string> { "R.E.P.O" },
				Slots = new List<int> { connectionInfoProvider.Slot },
				Tags = new List<string> { "EnergyLink" },
				Data = new Dictionary<string, JToken>
				{
					{ "deltaCurrency", EnergyLink.DeltaCurreny },
					{ "deltaTruckEnergy", EnergyLink.DeltaTruckEnergy },
					{ "time", EnergyLink.Timestamp.ToUnixTimeStamp() },
					{ "source", EnergyLink.Source }
				}
			};
			if (EnergyLink.Cause != null) {
				bouncePacket.Data.Add("cause", EnergyLink.Cause);
			}

			lastSendEnergyLink = EnergyLink;
			

			Debug.Log("Sending EnergyLink Packet");
			connection.session.Socket.SendPacketAsync(bouncePacket);
			Debug.Log("Sent EnergyLink Packet");

		}

		public BouncedPacket DebugGetAsBouncedPacket(EnergyLink EnergyLink) {
			BouncedPacket bouncePacket = new BouncedPacket {
				Games = new List<string> { "R.E.P.O" },
				Slots = new List<int> { connectionInfoProvider.Slot },
				Tags = new List<string> { "EnergyLink" },
				Data = new Dictionary<string, JToken>
				{
					{ "deltaCurrency", EnergyLink.DeltaCurreny },
					{ "deltaTruckEnergy", EnergyLink.DeltaTruckEnergy },
					{ "time", EnergyLink.Timestamp.ToUnixTimeStamp() },
					{ "source", EnergyLink.Source }
				}
			};
			if (EnergyLink.Cause != null) {
				bouncePacket.Data.Add("cause", EnergyLink.Cause);
			}

			return bouncePacket;
		}

		//
		// Summary:
		//     Adds "EnergyLink" to your Archipelago.MultiClient.Net.ArchipelagoSession's tags
		//     EnergyLinkService.OnEnergyLinkReceived
		//     events
		public void EnableEnergyLink() {
			if (Array.IndexOf(connectionInfoProvider.Tags, "EnergyLink") == -1) {
				connectionInfoProvider.UpdateConnectionOptions(connectionInfoProvider.Tags.Concat(new string[1] { "EnergyLink" }).ToArray());

				OnEnergyLinkReceived += (energyLinkObject) =>
				{
					// todo

					//Plugin.connection.
					Debug.Log("Recieved Energy Link");
					if (energyLinkObject != null) 
					{
						if (energyLinkObject.DeltaCurreny != 0) 
						{
							int currentCurrency = SemiFunc.StatGetRunCurrency();
							Debug.Log("Current currency is:" + currentCurrency.ToString());
							Debug.Log("Currency will be set to:" + (currentCurrency + energyLinkObject.DeltaCurreny).ToString());
							int returnCode = SemiFunc.StatSetRunCurrency(currentCurrency + energyLinkObject.DeltaCurreny);
							Debug.Log("StatSetRunCurrency returned: " + returnCode.ToString());

						}

						// Charging Station stuff
						// refer to https://thunderstore.io/c/repo/p/Jettcodey/Extract_Crystals_In_Levels/source/ for guidance
						// TODO check that visual and multiplayer connections work with this 
						if (energyLinkObject.DeltaTruckEnergy != 0)
						{
							
							// Update Charge Station save value
							if (StatsManager.instance.runStats.ContainsKey("chargingStationCharge")) // not to be confused with chargingStationChargeTotal, which is the total capacity
							{
								int currentCrystalEnergy = StatsManager.instance.runStats["chargingStationCharge"];
								StatsManager.instance.runStats["chargingStationCharge"] = currentCrystalEnergy + energyLinkObject.DeltaTruckEnergy;
							}
							else 
							{
								// TODO try to get this value from the session data, which should start at 1 if not altered yet
								int currentCrystalEnergy = 1;
								StatsManager.instance.runStats.Add("chargingStationCharge", currentCrystalEnergy + energyLinkObject.DeltaTruckEnergy);
							}

							/*
							// Update Charge Station Visually (if loaded)
							ChargingStation chargingStation = ChargingStation.instance;
							if (chargingStation != null) 
							{
								FieldInfo field = typeof(ChargingStation).GetField("chargeInt", BindingFlags.Instance | BindingFlags.NonPublic);
								if (field != null) {
									field.SetValue(chargingStation, newCrystalCount);
								}
								FieldInfo field2 = typeof(ChargingStation).GetField("chargeTotal", BindingFlags.Instance | BindingFlags.NonPublic);
								if (field2 != null) {
									int num = Mathf.Min(newCrystalCount * 10, 100);
									field2.SetValue(chargingStation, num);
								}
								FieldInfo field3 = typeof(ChargingStation).GetField("chargeFloat", BindingFlags.Instance | BindingFlags.NonPublic);
								if (field3 != null) {
									field3.SetValue(chargingStation, (float)newCrystalCount / 10f);
								}
								FieldInfo field4 = typeof(ChargingStation).GetField("chargeSegmentCurrent", BindingFlags.Instance | BindingFlags.NonPublic);
								if (field4 != null) {
									field4.SetValue(chargingStation, Mathf.RoundToInt((float)newCrystalCount / 10f * 40f));
								}
								FieldInfo field5 = typeof(ChargingStation).GetField("chargeScaleTarget", BindingFlags.Instance | BindingFlags.NonPublic);
								if (field5 != null) {
									field5.SetValue(chargingStation, (float)newCrystalCount / 10f);
								}
							}
							*/
						}
					}
				};
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
