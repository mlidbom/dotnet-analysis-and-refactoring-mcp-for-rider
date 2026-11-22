package dev.magnus.royder.remotecloseworktree;

import io.netty.buffer.Unpooled;
import io.netty.channel.Channel;
import io.netty.channel.ChannelFutureListener;
import io.netty.handler.codec.http.*;

import static java.nio.charset.StandardCharsets.UTF_8;
import static io.netty.handler.codec.http.HttpResponseStatus.OK;

/** A response to the supervisor. The ETag, when present, identifies one open IntelliJ Project. */
record WorktreeReply(HttpResponseStatus status, String message, String etag) {
   WorktreeReply(HttpResponseStatus status, String message) { this(status, message, null); }

   static WorktreeReply opened(String etag) { return new WorktreeReply(OK, "", etag); }

   void send(Channel channel, HttpVersion version, boolean keepAlive) {
      var response = new DefaultFullHttpResponse(version, status, Unpooled.copiedBuffer(message, UTF_8));
      response.headers().set(HttpHeaderNames.CONTENT_TYPE, "text/plain; charset=utf-8");
      response.headers().set(HttpHeaderNames.CACHE_CONTROL, "no-store");
      if (etag != null) response.headers().set(HttpHeaderNames.ETAG, etag);
      HttpUtil.setContentLength(response, response.content().readableBytes());
      HttpUtil.setKeepAlive(response, keepAlive);
      var sent = channel.writeAndFlush(response);
      if (!keepAlive) sent.addListener(ChannelFutureListener.CLOSE);
   }
}
