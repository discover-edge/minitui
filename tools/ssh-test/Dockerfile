# An SSH server for manual QA of MiniTui over SSH (see README.md). Local testing only: the login is test/test.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends openssh-server iproute2 procps tmux \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /run/sshd \
    && useradd --create-home --shell /bin/bash test \
    && echo 'test:test' | chpasswd \
    && printf 'PasswordAuthentication yes\nPermitRootLogin no\n' > /etc/ssh/sshd_config.d/minitui.conf \
    && printf 'export LANG=C.UTF-8\nexport PATH="$PATH:/opt/minitui"\n' > /etc/profile.d/minitui.sh

COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

EXPOSE 22
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
