FROM sbtscala/scala-sbt:eclipse-temurin-17.0.20_8_1.13.0_2.12.21 AS build
WORKDIR /build
COPY project project
COPY build.sbt .
RUN sbt update
COPY src src
RUN sbt assembly

FROM eclipse-temurin:17-jre-jammy AS runtime
ENV SPARK_VERSION=3.5.1
ENV SPARK_HOME=/opt/spark
ENV PATH=$PATH:$SPARK_HOME/bin

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && curl -fsSL "https://archive.apache.org/dist/spark/spark-${SPARK_VERSION}/spark-${SPARK_VERSION}-bin-hadoop3.tgz" -o /tmp/spark.tgz \
    && tar -xzf /tmp/spark.tgz -C /opt \
    && mv /opt/spark-${SPARK_VERSION}-bin-hadoop3 ${SPARK_HOME} \
    && rm /tmp/spark.tgz \
    && apt-get purge -y curl && apt-get autoremove -y && rm -rf /var/lib/apt/lists/*

COPY --from=build /build/target/scala-2.12/risk-engine.jar /app/risk-engine.jar

ENTRYPOINT ["spark-submit", "--master", "local[4]", \
  "--class", "frauddetection.risk.RiskEngineApp", \
  "/app/risk-engine.jar"]
