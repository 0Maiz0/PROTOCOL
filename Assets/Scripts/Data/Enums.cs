/// All shared enums for ETS Match Engine.

public enum TeamSide { Attacker, Defender }
public enum BuyType { Full, Force, Half, Eco }
public enum Role { Entry, Support, AWPer, Lurker, IGL }
public enum BotState { Idle, Moving, Peeking, Engaged, Shooting, Reloading, Dead }
public enum PCTier { Budget, Mid, HighEnd }
public enum MonitorTier { Hz60, Hz144, Hz240 }
public enum RoundPhase { FreezeTime, Live, PostRound }
public enum CameraMode { Overview, Follow, KillCam, Clutch, Free }
public enum BombState { NotPlanted, Planted, Exploded, Defused }
public enum StrategyZone { NodeA, Axis, NodeB, Split }
public enum StrategyType { Rush, Execute, Fake, Control, Default }
public enum MidRoundCall { Commit, Rotate, SlowPlay, Save, HoldPositions, RotateA, RotateB, AggressivePush, CTSave }
public enum OfferResult { Accepted, Countered, Rejected }
public enum BracketStage { Semifinal, Final, Done }
