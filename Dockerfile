FROM ubuntu:impish

ARG DEBIAN_FRONTEND=noninteractive

RUN set -ex \
    && sed -i -- 's/# deb-src/deb-src/g' /etc/apt/sources.list \
    && apt-get update \
    && apt-get install -y \
               build-essential \
               cdbs \
               devscripts \
               equivs \
               fakeroot \
    && apt-get install curl ca-certificates -y \
    && curl https://repo.waydro.id | bash \
    && apt-get install waydroid -y