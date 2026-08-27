name := "scala-risk-engine"
version := "0.1.0"
scalaVersion := "2.12.18"

val sparkVersion = "3.5.1"

libraryDependencies ++= Seq(
  "org.apache.spark" %% "spark-core" % sparkVersion % Provided,
  "org.apache.spark" %% "spark-sql" % sparkVersion % Provided,
  "org.apache.spark" %% "spark-sql-kafka-0-10" % sparkVersion,
  "org.apache.logging.log4j" % "log4j-core" % "2.20.0" % Provided,
  "org.postgresql" % "postgresql" % "42.7.3",
  "com.typesafe" % "config" % "1.4.3",
  "org.scalatest" %% "scalatest" % "3.2.18" % Test
)

Test / parallelExecution := false

assembly / mainClass := Some("frauddetection.risk.RiskEngineApp")
assembly / assemblyJarName := "risk-engine.jar"

assembly / assemblyMergeStrategy := {
  case PathList("META-INF", "services", _*)         => MergeStrategy.concat
  case PathList("META-INF", _*)                      => MergeStrategy.discard
  case "reference.conf"                               => MergeStrategy.concat
  case "module-info.class"                            => MergeStrategy.discard
  case x =>
    val oldStrategy = (assembly / assemblyMergeStrategy).value
    oldStrategy(x)
}
